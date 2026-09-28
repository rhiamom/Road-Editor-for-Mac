/**************************************************************************
 *   Road Editor for Mac                                                  *
 *   © 2026 GramzeSweatshop (rhiamom@mac.com). Written with Claude        *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                        *
 *                                                                        *
 *   One neighborhood opened for road editing: its terrain, its lots and  *
 *   its road squares. Reads and writes through Mootilda's handlers       *
 *   (R_NHTG, R_NHTR, R_DESC), unchanged.                                 *
 *                                                                        *
 *   Squares the game already wrote keep their bytes; a square only gets  *
 *   rebuilt when it is new, and a neighbour of an edited square only has *
 *   its connections retyped. So a road that meets a bridge keeps the     *
 *   connection bit it had toward that bridge.                            *
 *                                                                        *
 *   Lots: a lot's rectangle includes the strip of road along its front,  *
 *   and its orientation says which edge that is (0 = row x=Top, 2 = row  *
 *   x=Top+Width-1, 3 = column y=Left, 1 = column y=Left+Height-1). The   *
 *   game only lets a lot sit where that strip is road, so the front      *
 *   squares can be drawn on but never erased, and the rest of the lot    *
 *   takes no road at all.                                                *
 *************************************************************************/

using HoodReplace;
using SimPe.Interfaces.Files;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RoadEditor.Engine
{
    public enum Ground { Open, Water, Steep, Edge, Lot, LotFront }

    public sealed class LotInfo
    {
        public string Name = "";
        public int Top, Left, Width, Height, Orientation;
        public bool IsFront(int x, int y) => Orientation switch
        {
            0 => x == Top,
            2 => x == Top + Width - 1,
            3 => y == Left,
            _ => y == Left + Height - 1,
        };
        public bool Contains(int x, int y) =>
            x >= Top && x < Top + Width && y >= Left && y < Left + Height;
    }

    public sealed class RoadDocument
    {
        public const uint NHTG = 0xABCB5DA4, NHTR = 0xABD0DC63, LotDescription = 0x0BF999E7;
        public const float WaterLevel = 312.5f;

        /// <summary>
        /// Largest rise, corner to corner, allowed across one road square. The
        /// steepest square under a Maxis road (Veronaville) rises 9.19.
        /// </summary>
        public const float MaxRise = 10f;

        public string Path { get; }
        /// <summary>Terrain grid size in points; squares run 0..Width-2 by 0..Height-2.</summary>
        public int Width { get; }
        public int Height { get; }
        public float[,] Terrain { get; }          // [y, x]
        public List<LotInfo> Lots { get; } = new();

        private readonly List<(int x, int y)> _order = new();
        private Dictionary<(int x, int y), byte[]> _roads = new();
        private readonly Stack<Dictionary<(int, int), byte[]>> _undo = new();
        private DateTime _loadedWriteTime;

        public bool Dirty { get; private set; }
        public int RoadCount => _roads.Count;
        public int OriginalBridgeCount { get; }
        public bool CanUndo => _undo.Count > 0;

        public RoadDocument(string path)
        {
            Path = path;
            _loadedWriteTime = File.GetLastWriteTimeUtc(path);
            var pkg = SimPe.Packages.File.LoadFromFile(path);
            try
            {
                var tg = pkg.FindFile(NHTG, 0, 0xFFFFFFFF, 0)
                         ?? throw new InvalidDataException("This package has no neighborhood terrain.");
                var nhtg = new R_NHTG(pkg, tg);
                Width = nhtg.Width;
                Height = nhtg.Height;
                Terrain = nhtg.GetTerrain(0, 0, Height, Width);

                var tr = pkg.FindFile(NHTR, 0, 0xFFFFFFFF, 0)
                         ?? throw new InvalidDataException("This package has no road list.");
                var nhtr = new R_NHTR(pkg, tr);
                byte[] raw = nhtr.Roads;
                for (int i = 0; i < raw.Length / RoadTiles.Size; i++)
                {
                    byte[] tile = new byte[RoadTiles.Size];
                    Array.Copy(raw, i * RoadTiles.Size, tile, 0, RoadTiles.Size);
                    var sq = RoadTiles.SquareOf(tile);
                    if (_roads.ContainsKey(sq)) continue;   // never seen, but don't double up
                    _roads[sq] = tile;
                    _order.Add(sq);
                }
                OriginalBridgeCount = nhtr.Bridges.Length;

                foreach (IPackedFileDescriptor pfd in pkg.FindFiles(LotDescription))
                {
                    var d = new R_DESC(pkg, pfd, true);
                    if (d.LotType == 9) continue;   // apartment sublots share the base lot's ground
                    var lot = new LotInfo
                    {
                        Name = d.LotName ?? "", Top = d.Top, Left = d.Left,
                        Width = d.Width, Height = d.Height, Orientation = d.Orientation,
                    };
                    // Hidden lots (vacation, hobby, witch) sit outside the map; skip anything off-grid.
                    if (lot.Top < 0 || lot.Left < 0 || lot.Top + lot.Width > Width - 1 || lot.Left + lot.Height > Height - 1)
                        continue;
                    Lots.Add(lot);
                }
            }
            finally
            {
                pkg.ForgetUpdate();
                pkg.Close();
            }
        }

        // ---- reading -------------------------------------------------------

        public bool IsRoad(int x, int y) => _roads.ContainsKey((x, y));
        public int MaskAt(int x, int y) => _roads.TryGetValue((x, y), out var t) ? RoadTiles.MaskOf(t) : 0;
        public IEnumerable<(int x, int y)> Roads => _roads.Keys;

        public bool InGrid(int x, int y) => x >= 0 && y >= 0 && x < Width - 1 && y < Height - 1;

        public float Rise(int x, int y)
        {
            float a = Terrain[y, x], b = Terrain[y + 1, x], c = Terrain[y, x + 1], d = Terrain[y + 1, x + 1];
            return Math.Max(Math.Max(a, b), Math.Max(c, d)) - Math.Min(Math.Min(a, b), Math.Min(c, d));
        }

        public float LowestCorner(int x, int y) =>
            Math.Min(Math.Min(Terrain[y, x], Terrain[y + 1, x]), Math.Min(Terrain[y, x + 1], Terrain[y + 1, x + 1]));

        public LotInfo? LotAt(int x, int y) => Lots.FirstOrDefault(l => l.Contains(x, y));

        public Ground GroundAt(int x, int y)
        {
            if (x < 1 || y < 1 || x >= Width - 2 || y >= Height - 2) return Ground.Edge;
            var lot = LotAt(x, y);
            if (lot != null) return lot.IsFront(x, y) ? Ground.LotFront : Ground.Lot;
            if (LowestCorner(x, y) < WaterLevel) return Ground.Water;
            if (Rise(x, y) > MaxRise) return Ground.Steep;
            return Ground.Open;
        }

        /// <summary>Why a road can't go here, or null if it can.</summary>
        public string? WhyNotDraw(int x, int y)
        {
            switch (GroundAt(x, y))
            {
                case Ground.Edge: return "Too close to the edge of the neighborhood.";
                case Ground.Water: return "Water — roads over water need a bridge, which this version can't build.";
                case Ground.Steep: return $"Too steep for a road (rises {Rise(x, y):F1}; the limit is {MaxRise:F0}).";
                case Ground.Lot: return $"Inside the lot \"{LotAt(x, y)!.Name}\".";
                default: return null;
            }
        }

        /// <summary>Why this road square can't be removed, or null if it can.</summary>
        public string? WhyNotErase(int x, int y)
        {
            var lot = LotAt(x, y);
            if (lot != null && lot.IsFront(x, y))
                return $"This road is the front of the lot \"{lot.Name}\"; the lot needs it.";
            return null;
        }

        /// <summary>Road squares with no connection at all. The game has no piece for these.</summary>
        public List<(int x, int y)> Isolated() =>
            _roads.Where(kv => RoadTiles.MaskOf(kv.Value) == 0).Select(kv => kv.Key).ToList();

        // ---- editing -------------------------------------------------------

        /// <summary>Call once before each stroke so Undo takes back the whole stroke.</summary>
        public void BeginStroke() =>
            _undo.Push(_roads.ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone()));

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            _roads = _undo.Pop().ToDictionary(kv => ((int, int))kv.Key, kv => kv.Value);
            Dirty = true;
            return true;
        }

        /// <summary>Adds a road square. Returns null on success, or the reason it was refused.</summary>
        public string? Draw(int x, int y)
        {
            if (!InGrid(x, y)) return "Outside the neighborhood.";
            if (IsRoad(x, y)) return null;
            string? why = WhyNotDraw(x, y);
            if (why != null) return why;

            int mask = 0;
            foreach (var (dx, dy) in RoadTiles.Neighbours)
                if (IsRoad(x + dx, y + dy)) mask |= RoadTiles.BitToward(dx, dy);
            _roads[(x, y)] = RoadTiles.Build(x, y, mask, (gx, gy) => Terrain[gy, gx]);
            if (!_order.Contains((x, y))) _order.Add((x, y));

            foreach (var (dx, dy) in RoadTiles.Neighbours)
                if (_roads.TryGetValue((x + dx, y + dy), out var n))
                    RoadTiles.Retype(n, RoadTiles.MaskOf(n) | RoadTiles.BitToward(-dx, -dy));
            Dirty = true;
            return null;
        }

        /// <summary>Removes a road square. Returns null on success, or the reason it was refused.</summary>
        public string? Erase(int x, int y)
        {
            if (!IsRoad(x, y)) return null;
            string? why = WhyNotErase(x, y);
            if (why != null) return why;

            _roads.Remove((x, y));
            foreach (var (dx, dy) in RoadTiles.Neighbours)
                if (_roads.TryGetValue((x + dx, y + dy), out var n))
                    RoadTiles.Retype(n, RoadTiles.MaskOf(n) & ~RoadTiles.BitToward(-dx, -dy));
            Dirty = true;
            return null;
        }

        // ---- saving --------------------------------------------------------

        /// <summary>True if the game appears to be running; saving then could be overwritten by it.</summary>
        public static bool GameIsRunning() =>
            Process.GetProcesses().Any(p =>
            {
                try { return p.ProcessName.Contains("Sims 2", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });

        public bool ChangedOnDisk() => File.GetLastWriteTimeUtc(Path) != _loadedWriteTime;

        /// <summary>
        /// Writes the roads back, after copying the package to .bkp (the same
        /// backup HoodReplacer makes). Returns the backup path.
        /// </summary>
        public string Save()
        {
            var isolated = Isolated();
            if (isolated.Count > 0)
                throw new InvalidOperationException(
                    $"{isolated.Count} road square(s) connect to nothing, e.g. at {isolated[0].x},{isolated[0].y}. " +
                    "Extend them to another square or erase them first.");

            // Original order first (the game's), then squares added in this session.
            var order = _order.Where(_roads.ContainsKey).Distinct().ToList();
            var bytes = new List<byte>(order.Count * RoadTiles.Size);
            foreach (var sq in order) bytes.AddRange(_roads[sq]);

            var pkg = SimPe.Packages.File.LoadFromFile(Path);
            var nhtr = new R_NHTR(pkg, pkg.FindFile(NHTR, 0, 0xFFFFFFFF, 0));
            nhtr.Roads = bytes.ToArray();
            nhtr.Rewrite();

            string backup = System.IO.Path.ChangeExtension(Path, ".bkp");
            File.Copy(Path, backup, true);
            var ms = pkg.Build();
            pkg.Close();
            File.WriteAllBytes(Path, ms.ToArray());

            _loadedWriteTime = File.GetLastWriteTimeUtc(Path);
            _undo.Clear();
            Dirty = false;
            return backup;
        }
    }
}

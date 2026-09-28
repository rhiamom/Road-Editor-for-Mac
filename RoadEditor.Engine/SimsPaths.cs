/***************************************************************************
 *   Hood Checker for Mac                                                  *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 ***************************************************************************/

using System;
using System.IO;

namespace LotExpander
{
    /// <summary>
    /// Resolves the Sims 2 user folder on macOS. Checks the sandboxed App Store
    /// / Super Collection container first, then the older non-sandboxed location
    /// (same order the other Mac tools use — see Clean Installer / CC-Merger).
    /// </summary>
    public static class SimsPaths
    {
        private static string Home =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static readonly string[] Candidates =
        {
            "Library/Containers/com.aspyr.sims2.appstore/Data/Library/Application Support/Aspyr/The Sims 2",
            "Library/Application Support/Aspyr/The Sims 2",
        };

        /// <summary>The Sims 2 user folder, or null if none found.</summary>
        public static string? UserFolder
        {
            get
            {
                foreach (string c in Candidates)
                {
                    string full = Path.Combine(Home, c);
                    if (Directory.Exists(full))
                        return full;
                }
                return null;
            }
        }

        /// <summary>The Neighborhoods folder, or null if the user folder isn't found.</summary>
        public static string? NeighborhoodsFolder
        {
            get
            {
                string? user = UserFolder;
                if (user == null) return null;
                string nb = Path.Combine(user, "Neighborhoods");
                return Directory.Exists(nb) ? nb : null;
            }
        }
    }
}

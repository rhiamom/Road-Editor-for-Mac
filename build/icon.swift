// Draws the source art for build/AppIcon.icns (Road Editor for Mac).
// swift build/icon.swift icon.png  ->  sips -z 1024 1024  ->  .iconset  ->  iconutil -c icns
import AppKit
let N: CGFloat = 1024
let img = NSImage(size: NSSize(width: N, height: N))
img.lockFocus()
let ctx = NSGraphicsContext.current!.cgContext
func c(_ r: Int, _ g: Int, _ b: Int, _ a: CGFloat = 1) -> CGColor { NSColor(srgbRed: CGFloat(r)/255, green: CGFloat(g)/255, blue: CGFloat(b)/255, alpha: a).cgColor }
func grad(_ a: CGColor, _ b: CGColor, _ p0: CGPoint, _ p1: CGPoint) {
    let g = CGGradient(colorsSpace: nil, colors: [a, b] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(g, start: p0, end: p1, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
}
// macOS icon plate with soft shadow
let plate = CGPath(roundedRect: CGRect(x: 100, y: 100, width: 824, height: 824), cornerWidth: 185, cornerHeight: 185, transform: nil)
ctx.saveGState(); ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 28, color: c(0,0,0,0.35))
ctx.addPath(plate); ctx.setFillColor(.white); ctx.fillPath(); ctx.restoreGState()
ctx.addPath(plate); ctx.clip()
// Ground seen from above
grad(c(150, 196, 96), c(92, 146, 62), CGPoint(x: 0, y: 924), CGPoint(x: 0, y: 100))
// River running top-left to bottom-right
let river = CGMutablePath()
river.move(to: CGPoint(x: 100, y: 700)); river.addCurve(to: CGPoint(x: 924, y: 300), control1: CGPoint(x: 420, y: 720), control2: CGPoint(x: 600, y: 300))
river.addLine(to: CGPoint(x: 924, y: 150)); river.addCurve(to: CGPoint(x: 100, y: 540), control1: CGPoint(x: 560, y: 150), control2: CGPoint(x: 400, y: 560)); river.closeSubpath()
ctx.saveGState(); ctx.addPath(river); ctx.clip(); grad(c(80, 160, 220), c(40, 100, 180), CGPoint(x: 100, y: 700), CGPoint(x: 924, y: 150)); ctx.restoreGState()
// Road: bottom-left to top-right, crossing the river
let road = CGMutablePath()
road.move(to: CGPoint(x: 60, y: 250)); road.addCurve(to: CGPoint(x: 980, y: 800), control1: CGPoint(x: 400, y: 330), control2: CGPoint(x: 600, y: 760))
ctx.setLineCap(.butt)
ctx.addPath(road); ctx.setStrokeColor(c(62, 62, 66)); ctx.setLineWidth(118); ctx.strokePath()
// Bridge deck where the road crosses the water: stone-coloured with a rail each side
ctx.saveGState(); ctx.addPath(river); ctx.clip()
ctx.addPath(road); ctx.setStrokeColor(c(150, 118, 88)); ctx.setLineWidth(150); ctx.strokePath()
ctx.addPath(road); ctx.setStrokeColor(c(70, 70, 74)); ctx.setLineWidth(110); ctx.strokePath()
ctx.restoreGState()
// Dashed centre line
ctx.addPath(road); ctx.setStrokeColor(c(250, 244, 220)); ctx.setLineWidth(14); ctx.setLineDash(phase: 0, lengths: [42, 34]); ctx.strokePath()
img.unlockFocus()
let rep = NSBitmapImageRep(data: img.tiffRepresentation!)!
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))

import AppKit

let target = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
let entries = [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)]
for (size, scale) in entries {
    let pixels = size * scale
    let image = NSImage(size: NSSize(width: CGFloat(pixels), height: CGFloat(pixels)), flipped: false) { _ in
        let unit = CGFloat(pixels) / 100
        NSColor(srgbRed: 0.20, green: 0.43, blue: 0.39, alpha: 1).setFill()
        NSBezierPath(roundedRect: NSRect(x: 5*unit, y: 5*unit, width: 90*unit, height: 90*unit), xRadius: 23*unit, yRadius: 23*unit).fill()
        for petal in 0..<8 {
            let angle = Double(petal) * Double.pi / 4
            let x = CGFloat(50 + 22 * cos(angle)) * unit
            let y = CGFloat(50 + 22 * sin(angle)) * unit
            NSColor(srgbRed: 0.92, green: 0.97, blue: 0.91, alpha: 1).setFill()
            NSBezierPath(ovalIn: NSRect(x: x - 6*unit, y: y - 6*unit, width: 12*unit, height: 12*unit)).fill()
        }
        NSColor(srgbRed: 0.96, green: 0.79, blue: 0.47, alpha: 1).setFill()
        NSBezierPath(ovalIn: NSRect(x: 44*unit, y: 44*unit, width: 12*unit, height: 12*unit)).fill()
        return true
    }
    var bounds = NSRect(origin: .zero, size: image.size)
    guard let cgImage = image.cgImage(forProposedRect: &bounds, context: nil, hints: nil),
          let png = NSBitmapImageRep(cgImage: cgImage).representation(using: .png, properties: [:]) else {
        fatalError("Could not render the application icon")
    }
    let name = "icon_\(size)x\(size)\(scale == 2 ? "@2x" : "").png"
    try png.write(to: target.appendingPathComponent(name))
}

import AppKit

let source = URL(fileURLWithPath: CommandLine.arguments[1])
let target = URL(fileURLWithPath: CommandLine.arguments[2])
guard let logo = NSImage(contentsOf: source) else {
    fatalError("Could not open the FocusBloom icon")
}
try FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
let entries = [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)]
for (size, scale) in entries {
    let pixels = CGFloat(size * scale)
    let image = NSImage(size: NSSize(width: pixels, height: pixels), flipped: false) { rect in
        NSGraphicsContext.current?.imageInterpolation = .high
        logo.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1)
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

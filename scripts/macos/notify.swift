import Foundation
import UserNotifications

public typealias Completion = @convention(c) (Int32, UnsafeMutableRawPointer?) -> Void

@_cdecl("focusbloom_notify")
public func notify(_ action: Int32, _ title: UnsafePointer<CChar>?, _ body: UnsafePointer<CChar>?, _ context: UnsafeMutableRawPointer?, _ callback: @escaping Completion) {
    let center = UNUserNotificationCenter.current()
    if action == 0 {
        center.requestAuthorization(options: [.alert, .sound, .badge]) { granted, _ in
            callback(granted ? 0 : 2, context)
        }
    } else {
        let heading = title.map { String(cString: $0) } ?? "FocusBloom"
        let detail = body.map { String(cString: $0) } ?? ""
        center.getNotificationSettings { settings in
            guard settings.authorizationStatus == .authorized || settings.authorizationStatus == .provisional else { callback(2, context); return }
            let content = UNMutableNotificationContent()
            content.title = heading
            content.body = detail
            content.sound = .default
            center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)) { error in
                callback(error == nil ? 0 : 1, context)
            }
        }
    }
}

import AVFoundation
import AppKit
import ImageIO

private var framePath = ""
private var movieGenerator: AVAssetImageGenerator?
private var gifSource: CGImageSource?
private var mediaDuration: Double = 1

@_cdecl("focusbloom_frame")
public func frame(_ input: UnsafePointer<CChar>, _ seconds: Double, _ width: UnsafeMutablePointer<Int32>, _ height: UnsafeMutablePointer<Int32>) -> UnsafeMutableRawPointer? {
    let path = String(cString: input)
    let url = URL(fileURLWithPath: path)
    if framePath != path {
        framePath = path
        movieGenerator = nil
        gifSource = nil
        if url.pathExtension.lowercased() == "gif" {
            gifSource = CGImageSourceCreateWithURL(url as CFURL, nil)
        } else {
            let asset = AVURLAsset(url: url)
            mediaDuration = CMTimeGetSeconds(asset.duration)
            let generator = AVAssetImageGenerator(asset: asset)
            generator.appliesPreferredTrackTransform = true
            generator.maximumSize = CGSize(width: 1280, height: 720)
            movieGenerator = generator
        }
    }
    var image: CGImage?
    if let source = gifSource {
        let count = CGImageSourceGetCount(source)
        var durations: [Double] = []
        for index in 0..<count {
            let props = CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [CFString: Any]
            let gif = props?[kCGImagePropertyGIFDictionary] as? [CFString: Any]
            durations.append(max(0.02, gif?[kCGImagePropertyGIFDelayTime] as? Double ?? 0.1))
        }
        let total = durations.reduce(0, +)
        if total <= 0 { return nil }
        var at = max(0, seconds).truncatingRemainder(dividingBy: total)
        var selected = 0
        while selected + 1 < durations.count && at >= durations[selected] { at -= durations[selected]; selected += 1 }
        image = CGImageSourceCreateImageAtIndex(source, selected, nil)
    } else if let generator = movieGenerator, mediaDuration.isFinite && mediaDuration > 0 {
        let at = max(0, seconds).truncatingRemainder(dividingBy: mediaDuration)
        image = try? generator.copyCGImage(at: CMTime(seconds: at, preferredTimescale: 600), actualTime: nil)
    }
    guard let image = image else { return nil }
    let factor = min(1, 1280 / Double(image.width), 720 / Double(image.height))
    let w = max(1, Int(Double(image.width) * factor))
    let h = max(1, Int(Double(image.height) * factor))
    guard let pixels = malloc(w * h * 4) else { return nil }
    let bitmap = CGBitmapInfo.byteOrder32Little.rawValue | CGImageAlphaInfo.premultipliedFirst.rawValue
    guard let context = CGContext(data: pixels, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: bitmap) else { free(pixels); return nil }
    context.draw(image, in: CGRect(x: 0, y: 0, width: CGFloat(w), height: CGFloat(h)))
    width.pointee = Int32(w)
    height.pointee = Int32(h)
    return pixels
}

@_cdecl("focusbloom_frame_free")
public func freeFrame(_ pixels: UnsafeMutableRawPointer?) { free(pixels) }

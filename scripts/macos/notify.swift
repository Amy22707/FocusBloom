import Foundation
import UserNotifications

public typealias Completion = @convention(c) (Int32, UnsafeMutableRawPointer?) -> Void

@_cdecl("focusbloom_notify")
public func notify(_ action: Int32, _ title: UnsafePointer<CChar>?, _ body: UnsafePointer<CChar>?, _ context: UnsafeMutableRawPointer?, _ callback: Completion) {
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

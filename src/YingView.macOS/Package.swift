// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "YingView",
    platforms: [.macOS(.v13)],
    products: [.library(name: "YingViewCore", targets: ["YingViewCore"]), .executable(name: "YingView", targets: ["YingViewApp"])],
    targets: [
        .target(name: "YingViewCore", path: "Sources/YingViewCore"),
        .executableTarget(name: "YingViewApp", dependencies: ["YingViewCore"], path: "Sources/YingView"),
        .testTarget(name: "YingViewTests", dependencies: ["YingViewCore"], path: "Tests/YingViewTests")
    ]
)

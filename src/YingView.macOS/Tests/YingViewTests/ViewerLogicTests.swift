import XCTest
import YingViewCore

final class ViewerLogicTests: XCTestCase {
    func testContainFitsWholeImageAndEnlargesSmallImage() {
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: 4000, imageHeight: 2000, viewWidth: 1000, viewHeight: 800), 0.25)
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: 100, imageHeight: 50, viewWidth: 1000, viewHeight: 800), 10)
    }
    func testContainAccountsForQuarterTurns() {
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: 4000, imageHeight: 2000, viewWidth: 1000, viewHeight: 800, quarterTurns: 1), 0.2)
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: 4000, imageHeight: 2000, viewWidth: 1000, viewHeight: 800, quarterTurns: 2), 0.25)
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: 4000, imageHeight: 2000, viewWidth: 1000, viewHeight: 800, quarterTurns: -1), 0.2)
    }
    func testActualSizeUsesPhysicalPixels() {
        XCTAssertEqual(ViewerLogic.actualScale(backingScale: 2), 0.5)
        XCTAssertEqual(ViewerLogic.actualScale(backingScale: 1), 1)
        XCTAssertEqual(ViewerLogic.actualScale(backingScale: 0), 1)
        XCTAssertEqual(ViewerLogic.actualScale(backingScale: .infinity), 1)
    }
    func testContainInvalidGeometryUsesNeutralScale() {
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: 0, imageHeight: 10, viewWidth: 100, viewHeight: 100), 1)
        XCTAssertEqual(ViewerLogic.containScale(imageWidth: .infinity, imageHeight: 10, viewWidth: 100, viewHeight: 100), 1)
    }
    func testNaturalSortOrdersNumberedNamesNaturally() {
        let urls = ["frame10.png", "frame2.png", "frame1.png"].map { URL(fileURLWithPath: "/tmp/\($0)") }
        XCTAssertEqual(ViewerLogic.naturalSort(urls).map(\.lastPathComponent), ["frame1.png", "frame2.png", "frame10.png"])
    }
    func testDeletionNavigationClampsAndEmpties() {
        XCTAssertEqual(ViewerLogic.deletionResult(remainingCount: 2, deletedIndex: 1).index, 1)
        XCTAssertEqual(ViewerLogic.deletionResult(remainingCount: 2, deletedIndex: 2).index, 1)
        XCTAssertEqual(ViewerLogic.deletionResult(remainingCount: 2, deletedIndex: -2).index, 0)
        XCTAssertTrue(ViewerLogic.deletionResult(remainingCount: 0, deletedIndex: 0).isEmpty)
    }
}

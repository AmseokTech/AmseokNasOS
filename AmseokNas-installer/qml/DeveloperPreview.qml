//--------------------------//
//--------提供与生产执行器隔离的安装器模拟会话---------//
//--------Provides a simulated installer session isolated from production execution--------//
//-------------------------//
import QtQuick

Main {
    id: previewRoot

    property QtObject previewSession: QtObject {
        id: simulatedSession

        property int currentStep: 0
        readonly property bool canGoBack: simulatedSession.currentStep > 0
        readonly property bool canGoForward: simulatedSession.currentStep < 2
        readonly property bool canStartInstallation: true
        readonly property bool executionEnabled: true
        readonly property bool developerPreview: true
        readonly property string distribution: "trixie"
        readonly property string architecture: "amd64"
        readonly property bool hasSystemDisk: true
        readonly property string systemDiskDisplayName: qsTr("模拟 NVMe SSD")
        readonly property string systemDiskStableId: "preview-wwn-0x5000c50000000001"
        readonly property string systemDiskCapacity: "256 GB"
        readonly property string validationMessage: ""
        property string statusMessage: ""

        function goBack(): void {
            simulatedSession.currentStep = Math.max(0, simulatedSession.currentStep - 1)
            simulatedSession.statusMessage = ""
        }

        function goForward(): void {
            simulatedSession.currentStep = Math.min(2, simulatedSession.currentStep + 1)
            simulatedSession.statusMessage = ""
        }

        function startInstallation(): void {
            simulatedSession.statusMessage = qsTr("已模拟开始安装；没有访问或修改任何磁盘")
        }
    }

    installerSession: previewRoot.previewSession
    windowedPreview: true
}

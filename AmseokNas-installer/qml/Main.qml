//--------------------------//
//--------承载深色 AmseokOS 安装器窗口与页面导航---------//
//--------Hosts the dark AmseokOS installer window and page navigation--------//
//-------------------------//
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "components"
import "pages"

ApplicationWindow {
    id: root

    required property var installerSession
    required property bool windowedPreview

    width: 1180
    height: 760
    minimumWidth: 960
    minimumHeight: 640
    visible: true
    visibility: windowedPreview ? Window.Windowed : Window.FullScreen
    title: qsTr("AmseokOS 安装程序")
    color: "#080808"

    background: Rectangle {
        color: "#080808"
    }

    Rectangle {
        id: cardOutline

        anchors.centerIn: parent
        width: installerCard.width + 2
        height: installerCard.height + 2
        radius: installerCard.radius + 1
        color: "#0a0a0a"
        border.width: 1
        border.color: "#4a4a4d"
    }

    Rectangle {
        id: installerCard

        anchors.centerIn: parent
        width: root.installerSession.currentStep === 0
               ? Math.min(parent.width - 120, 760)
               : Math.min(parent.width - 96, 1040)
        height: root.installerSession.currentStep === 0
                ? Math.min(parent.height - 100, 610)
                : Math.min(parent.height - 84, 676)
        radius: 20
        color: "#272727"
        clip: true

        Behavior on width {
            NumberAnimation {
                duration: 180
                easing.type: Easing.OutCubic
            }
        }

        Behavior on height {
            NumberAnimation {
                duration: 180
                easing.type: Easing.OutCubic
            }
        }

        WelcomePage {
            anchors.fill: parent
            visible: root.installerSession.currentStep === 0
            session: root.installerSession
            onInstallRequested: root.installerSession.goForward()
        }

        RowLayout {
            anchors.fill: parent
            visible: root.installerSession.currentStep > 0
            spacing: 0

            StepRail {
                Layout.preferredWidth: 230
                Layout.fillHeight: true
                currentStep: root.installerSession.currentStep
                developerPreview: root.installerSession.developerPreview
            }

            ColumnLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                spacing: 0

                StackLayout {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    currentIndex: Math.max(0, root.installerSession.currentStep - 1)

                    SystemDiskPage {
                        session: root.installerSession
                    }

                    ReviewPage {
                        session: root.installerSession
                    }
                }

                Rectangle {
                    Layout.fillWidth: true
                    Layout.preferredHeight: 82
                    color: "#222222"
                    border.width: 1
                    border.color: "#3b3b3d"

                    RowLayout {
                        anchors.fill: parent
                        anchors.leftMargin: 34
                        anchors.rightMargin: 34
                        spacing: 18

                        Button {
                            id: backButton

                            visible: root.installerSession.canGoBack
                            text: qsTr("返回")
                            hoverEnabled: true
                            implicitWidth: 92
                            implicitHeight: 38
                            onClicked: root.installerSession.goBack()

                            contentItem: Text {
                                text: backButton.text
                                color: "#f5f5f7"
                                font.pixelSize: 14
                                horizontalAlignment: Text.AlignHCenter
                                verticalAlignment: Text.AlignVCenter
                            }

                            background: Rectangle {
                                radius: 8
                                color: backButton.down
                                       ? "#3a3a3c"
                                       : (backButton.hovered ? "#454547" : "#363638")
                                border.width: 1
                                border.color: "#505054"
                            }
                        }

                        Item {
                            Layout.fillWidth: true
                        }

                        Text {
                            visible: root.installerSession.statusMessage.length > 0
                            Layout.maximumWidth: 330
                            text: root.installerSession.statusMessage
                            color: root.installerSession.developerPreview ? "#64a8ff" : "#ff9f8f"
                            font.pixelSize: 13
                            horizontalAlignment: Text.AlignRight
                            wrapMode: Text.WordWrap
                        }

                        PrimaryButton {
                            visible: root.installerSession.canGoForward
                            text: qsTr("继续")
                            enabled: true
                            onClicked: root.installerSession.goForward()
                        }

                        PrimaryButton {
                            visible: !root.installerSession.canGoForward
                            text: qsTr("开始安装")
                            enabled: root.installerSession.canStartInstallation
                            onClicked: root.installerSession.startInstallation()
                        }
                    }
                }
            }
        }

        Rectangle {
            visible: root.installerSession.developerPreview
            anchors.top: parent.top
            anchors.right: parent.right
            anchors.margins: 18
            width: previewLabel.implicitWidth + 24
            height: 30
            radius: 15
            color: "#183b64"
            border.width: 1
            border.color: "#2f6cae"
            z: 10

            Text {
                id: previewLabel

                anchors.centerIn: parent
                text: qsTr("开发者预览 · 模拟数据")
                color: "#b9d9ff"
                font.pixelSize: 12
                font.weight: Font.Medium
            }
        }
    }
}

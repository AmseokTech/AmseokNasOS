//--------------------------//
//--------承载统一的 AmseokOS 安装器窗口与页面导航---------//
//--------Hosts the unified AmseokOS installer window and page navigation--------//
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
    title: qsTr("AmseokOS 安装器")
    color: "#0b1530"

    background: Rectangle {
        gradient: Gradient {
            GradientStop {
                position: 0.0
                color: "#09142c"
            }
            GradientStop {
                position: 0.48
                color: "#17396c"
            }
            GradientStop {
                position: 1.0
                color: "#6d8eb0"
            }
        }

        Rectangle {
            width: parent.width * 0.58
            height: width
            radius: width / 2
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.rightMargin: -width * 0.22
            anchors.topMargin: -height * 0.38
            color: "#26a7d6ff"
        }
    }

    Rectangle {
        id: installerCard

        anchors.centerIn: parent
        width: Math.min(parent.width - 80, 1080)
        height: Math.min(parent.height - 80, 680)
        radius: 26
        color: "#f5f8fc"
        border.width: 1
        border.color: "#70ffffff"

        RowLayout {
            anchors.fill: parent
            spacing: 0

            StepRail {
                Layout.preferredWidth: 245
                Layout.fillHeight: true
                currentStep: root.installerSession.currentStep
            }

            ColumnLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                spacing: 0

                StackLayout {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    currentIndex: root.installerSession.currentStep

                    WelcomePage {
                        session: root.installerSession
                    }
                    SystemDiskPage {}
                    ReviewPage {
                        session: root.installerSession
                    }
                }

                Rectangle {
                    Layout.fillWidth: true
                    Layout.preferredHeight: 84
                    color: "#f8fafc"
                    border.width: 1
                    border.color: "#e4eaf2"

                    RowLayout {
                        anchors.fill: parent
                        anchors.leftMargin: 42
                        anchors.rightMargin: 42

                        Button {
                            visible: root.installerSession.canGoBack
                            text: qsTr("返回")
                            flat: true
                            onClicked: root.installerSession.goBack()
                        }

                        Item {
                            Layout.fillWidth: true
                        }

                        Text {
                            visible: root.installerSession.statusMessage.length > 0
                            text: root.installerSession.statusMessage
                            color: "#b42318"
                            font.pixelSize: 13
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
    }
}

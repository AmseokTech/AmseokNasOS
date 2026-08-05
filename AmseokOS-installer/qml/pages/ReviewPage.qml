//--------------------------//
//--------展示安装计划缺口并阻止未通过校验的执行---------//
//--------Shows plan gaps and prevents execution until validation succeeds--------//
//-------------------------//
import QtQuick
import QtQuick.Layouts

Item {
    id: pageRoot

    required property var session

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 54
        spacing: 18

        Text {
            text: qsTr("安装摘要")
            color: "#f5f5f7"
            font.pixelSize: 30
            font.weight: Font.DemiBold
        }

        Text {
            text: qsTr("确认所有项目后，安装器才会允许进入不可逆阶段。")
            color: "#b0b0b5"
            font.pixelSize: 15
        }

        GridLayout {
            Layout.fillWidth: true
            Layout.topMargin: 18
            columns: 2
            columnSpacing: 32
            rowSpacing: 18

            Text {
                text: qsTr("系统基线")
                color: "#9d9da2"
                font.pixelSize: 14
            }
            Text {
                text: "Debian " + pageRoot.session.distribution
                color: "#f5f5f7"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("架构")
                color: "#9d9da2"
                font.pixelSize: 14
            }
            Text {
                text: pageRoot.session.architecture
                color: "#f5f5f7"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("系统文件系统")
                color: "#9d9da2"
                font.pixelSize: 14
            }
            Text {
                text: "ext4"
                color: "#f5f5f7"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("系统盘")
                color: "#9d9da2"
                font.pixelSize: 14
            }
            Text {
                text: pageRoot.session.hasSystemDisk
                      ? qsTr("%1（%2）").arg(pageRoot.session.systemDiskDisplayName).arg(pageRoot.session.systemDiskCapacity)
                      : qsTr("尚未选择")
                color: pageRoot.session.hasSystemDisk ? "#f5f5f7" : "#ff9f8f"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("执行器")
                color: "#9d9da2"
                font.pixelSize: 14
            }
            Text {
                text: pageRoot.session.developerPreview
                      ? qsTr("仅模拟")
                      : (pageRoot.session.executionEnabled ? qsTr("可用") : qsTr("安全关闭"))
                color: pageRoot.session.executionEnabled ? "#62d894" : "#ffd28a"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
        }

        Rectangle {
            visible: pageRoot.session.validationMessage.length > 0
            Layout.fillWidth: true
            Layout.preferredHeight: visible ? 86 : 0
            Layout.topMargin: 18
            radius: 14
            color: "#4a2420"
            border.width: 1
            border.color: "#74433d"

            Text {
                anchors.fill: parent
                anchors.margins: 18
                verticalAlignment: Text.AlignVCenter
                text: pageRoot.session.validationMessage
                color: "#ffb4aa"
                font.pixelSize: 14
                wrapMode: Text.WordWrap
            }
        }

        Item {
            Layout.fillHeight: true
        }
    }
}

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
            color: "#152238"
            font.pixelSize: 30
            font.weight: Font.DemiBold
        }

        Text {
            text: qsTr("确认所有项目后，安装器才会允许进入不可逆阶段。")
            color: "#667085"
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
                color: "#667085"
                font.pixelSize: 14
            }
            Text {
                text: "Debian " + pageRoot.session.distribution
                color: "#1d2939"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("架构")
                color: "#667085"
                font.pixelSize: 14
            }
            Text {
                text: pageRoot.session.architecture
                color: "#1d2939"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("系统文件系统")
                color: "#667085"
                font.pixelSize: 14
            }
            Text {
                text: "ext4"
                color: "#1d2939"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("系统盘")
                color: "#667085"
                font.pixelSize: 14
            }
            Text {
                text: qsTr("尚未选择")
                color: "#b42318"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
            Text {
                text: qsTr("执行器")
                color: "#667085"
                font.pixelSize: 14
            }
            Text {
                text: pageRoot.session.executionEnabled ? qsTr("可用") : qsTr("安全关闭")
                color: pageRoot.session.executionEnabled ? "#067647" : "#b54708"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 86
            Layout.topMargin: 18
            radius: 14
            color: "#fff1f0"

            Text {
                anchors.fill: parent
                anchors.margins: 18
                verticalAlignment: Text.AlignVCenter
                text: pageRoot.session.validationMessage
                color: "#a12b24"
                font.pixelSize: 14
                wrapMode: Text.WordWrap
            }
        }

        Item {
            Layout.fillHeight: true
        }
    }
}

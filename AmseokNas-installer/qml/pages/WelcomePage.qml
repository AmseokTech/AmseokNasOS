//--------------------------//
//--------说明镜像基线与当前只读预览状态---------//
//--------Explains the image baseline and current read-only preview state--------//
//-------------------------//
import QtQuick
import QtQuick.Layouts

Item {
    id: pageRoot

    required property var session

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 54
        spacing: 20

        Item {
            Layout.fillHeight: true
        }

        Rectangle {
            Layout.alignment: Qt.AlignHCenter
            Layout.preferredWidth: 88
            Layout.preferredHeight: 88
            radius: 26
            color: "#e8f2ff"

            Text {
                anchors.centerIn: parent
                text: "A"
                color: "#1f6fbd"
                font.pixelSize: 46
                font.bold: true
            }
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            text: qsTr("安装 AmseokOS")
            color: "#152238"
            font.pixelSize: 34
            font.weight: Font.DemiBold
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            Layout.maximumWidth: 560
            horizontalAlignment: Text.AlignHCenter
            text: qsTr("基于 Debian %1，为 NAS 管理服务准备独立的系统盘。当前版本仅建立界面与安全边界，不会执行真实安装。").arg(pageRoot.session.distribution)
            color: "#667085"
            font.pixelSize: 16
            lineHeight: 1.4
            wrapMode: Text.WordWrap
        }

        Rectangle {
            Layout.alignment: Qt.AlignHCenter
            Layout.preferredWidth: 500
            Layout.preferredHeight: 58
            radius: 14
            color: "#eef5ff"

            Text {
                anchors.centerIn: parent
                text: qsTr("Debian %1 · %2 · ext4").arg(pageRoot.session.distribution).arg(pageRoot.session.architecture)
                color: "#285f9d"
                font.pixelSize: 14
                font.weight: Font.Medium
            }
        }

        Item {
            Layout.fillHeight: true
        }
    }
}

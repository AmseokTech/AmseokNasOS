//--------------------------//
//--------展示系统盘入口且在探测适配器完成前保持无写入---------//
//--------Shows the system-disk entry while remaining write-free until inventory exists--------//
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
            text: qsTr("选择系统盘")
            color: "#f5f5f7"
            font.pixelSize: 30
            font.weight: Font.DemiBold
        }

        Text {
            Layout.fillWidth: true
            text: qsTr("系统只会安装到明确选择并再次确认的磁盘，其他磁盘必须保持不变。")
            color: "#b0b0b5"
            font.pixelSize: 15
            wrapMode: Text.WordWrap
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 210
            Layout.topMargin: 20
            radius: 18
            color: "#303032"
            border.width: 1
            border.color: "#48484a"

            ColumnLayout {
                anchors.centerIn: parent
                width: Math.min(parent.width - 80, 480)
                spacing: 13

                Rectangle {
                    Layout.alignment: Qt.AlignHCenter
                    Layout.preferredWidth: 52
                    Layout.preferredHeight: 52
                    radius: 16
                    color: pageRoot.session.hasSystemDisk ? "#183f2b" : "#3a3a3c"

                    Text {
                        anchors.centerIn: parent
                        text: pageRoot.session.hasSystemDisk ? "✓" : "—"
                        color: pageRoot.session.hasSystemDisk ? "#62d894" : "#a1a1a6"
                        font.pixelSize: pageRoot.session.hasSystemDisk ? 24 : 28
                    }
                }

                Text {
                    Layout.alignment: Qt.AlignHCenter
                    text: pageRoot.session.hasSystemDisk
                          ? pageRoot.session.systemDiskDisplayName
                          : qsTr("磁盘探测尚未连接")
                    color: "#f5f5f7"
                    font.pixelSize: 17
                    font.weight: Font.DemiBold
                }

                Text {
                    Layout.fillWidth: true
                    horizontalAlignment: Text.AlignHCenter
                    text: pageRoot.session.hasSystemDisk
                          ? qsTr("%1 · %2").arg(pageRoot.session.systemDiskCapacity).arg(pageRoot.session.systemDiskStableId)
                          : qsTr("后续只接受稳定设备 ID、型号、序列号与容量均已复核的候选系统盘。")
                    color: "#a1a1a6"
                    font.pixelSize: 13
                    wrapMode: Text.WordWrap
                }
            }
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 72
            radius: 14
            color: pageRoot.session.developerPreview ? "#162f4e" : "#453519"
            border.width: 1
            border.color: pageRoot.session.developerPreview ? "#295b91" : "#765b2b"

            Text {
                anchors.fill: parent
                anchors.margins: 18
                verticalAlignment: Text.AlignVCenter
                text: pageRoot.session.developerPreview
                      ? qsTr("上方磁盘是界面模拟数据，不对应本机设备，也不会触发系统调用。")
                      : qsTr("当前不会扫描、分区、格式化或挂载任何真实设备。")
                color: pageRoot.session.developerPreview ? "#a8ceff" : "#ffd28a"
                font.pixelSize: 14
                wrapMode: Text.WordWrap
            }
        }

        Item {
            Layout.fillHeight: true
        }
    }
}

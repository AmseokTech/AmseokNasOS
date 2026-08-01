//--------------------------//
//--------展示系统盘入口且在探测适配器完成前保持无写入---------//
//--------Shows the system-disk entry while remaining write-free until inventory exists--------//
//-------------------------//
import QtQuick
import QtQuick.Layouts

Item {
    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 54
        spacing: 18

        Text {
            text: qsTr("选择系统盘")
            color: "#152238"
            font.pixelSize: 30
            font.weight: Font.DemiBold
        }

        Text {
            Layout.fillWidth: true
            text: qsTr("系统只会安装到明确选择并再次确认的磁盘，其他磁盘必须保持不变。")
            color: "#667085"
            font.pixelSize: 15
            wrapMode: Text.WordWrap
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 210
            Layout.topMargin: 20
            radius: 18
            color: "#f8fafc"
            border.width: 1
            border.color: "#d8e1ec"

            ColumnLayout {
                anchors.centerIn: parent
                width: Math.min(parent.width - 80, 480)
                spacing: 13

                Rectangle {
                    Layout.alignment: Qt.AlignHCenter
                    Layout.preferredWidth: 52
                    Layout.preferredHeight: 52
                    radius: 16
                    color: "#e8eef6"

                    Text {
                        anchors.centerIn: parent
                        text: "—"
                        color: "#667085"
                        font.pixelSize: 28
                    }
                }

                Text {
                    Layout.alignment: Qt.AlignHCenter
                    text: qsTr("磁盘探测尚未连接")
                    color: "#344054"
                    font.pixelSize: 17
                    font.weight: Font.DemiBold
                }

                Text {
                    Layout.fillWidth: true
                    horizontalAlignment: Text.AlignHCenter
                    text: qsTr("后续只接受稳定设备 ID、型号、序列号与容量均已复核的候选系统盘。")
                    color: "#7b8798"
                    font.pixelSize: 13
                    wrapMode: Text.WordWrap
                }
            }
        }

        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 72
            radius: 14
            color: "#fff7e6"

            Text {
                anchors.fill: parent
                anchors.margins: 18
                verticalAlignment: Text.AlignVCenter
                text: qsTr("当前不会扫描、分区、格式化或挂载任何真实设备。")
                color: "#8a570d"
                font.pixelSize: 14
                wrapMode: Text.WordWrap
            }
        }

        Item {
            Layout.fillHeight: true
        }
    }
}

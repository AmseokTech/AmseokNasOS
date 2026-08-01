//--------------------------//
//--------展示安装流程阶段且不承载业务状态转换---------//
//--------Displays installer stages without owning business-state transitions--------//
//-------------------------//
import QtQuick
import QtQuick.Layouts

Rectangle {
    id: root

    required property int currentStep

    color: "#101f3f"
    radius: 26

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 30
        spacing: 0

        RowLayout {
            spacing: 12

            Rectangle {
                Layout.preferredWidth: 38
                Layout.preferredHeight: 38
                radius: 12
                color: "#dcecff"

                Text {
                    anchors.centerIn: parent
                    text: "A"
                    color: "#164c8c"
                    font.pixelSize: 22
                    font.bold: true
                }
            }

            Text {
                text: "AmseokOS"
                color: "#ffffff"
                font.pixelSize: 20
                font.weight: Font.DemiBold
            }
        }

        Item {
            Layout.preferredHeight: 52
        }

        Repeater {
            model: [
                {
                    "label": qsTr("欢迎"),
                    "number": 1,
                    "reached": root.currentStep >= 0,
                    "selected": root.currentStep === 0
                },
                {
                    "label": qsTr("系统盘"),
                    "number": 2,
                    "reached": root.currentStep >= 1,
                    "selected": root.currentStep === 1
                },
                {
                    "label": qsTr("安装摘要"),
                    "number": 3,
                    "reached": root.currentStep >= 2,
                    "selected": root.currentStep === 2
                }
            ]

            delegate: RowLayout {
                id: stepRow

                required property var modelData

                Layout.fillWidth: true
                Layout.preferredHeight: 48
                spacing: 13

                Rectangle {
                    Layout.preferredWidth: 26
                    Layout.preferredHeight: 26
                    radius: 13
                    color: stepRow.modelData.reached ? "#dcecff" : "#26395d"

                    Text {
                        anchors.centerIn: parent
                        text: stepRow.modelData.number
                        color: stepRow.modelData.reached ? "#164c8c" : "#91a4c5"
                        font.pixelSize: 13
                        font.bold: true
                    }
                }

                Text {
                    text: stepRow.modelData.label
                    color: stepRow.modelData.selected ? "#ffffff" : "#91a4c5"
                    font.pixelSize: 15
                    font.weight: stepRow.modelData.selected ? Font.DemiBold : Font.Normal
                }
            }
        }

        Item {
            Layout.fillHeight: true
        }

        Text {
            Layout.fillWidth: true
            text: qsTr("安全预览 · 不会修改磁盘")
            color: "#8fa6c9"
            font.pixelSize: 12
            wrapMode: Text.WordWrap
        }
    }
}

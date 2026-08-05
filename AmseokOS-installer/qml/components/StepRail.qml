//--------------------------//
//--------展示安装流程阶段且不承载业务状态转换---------//
//--------Displays installer stages without owning business-state transitions--------//
//-------------------------//
import QtQuick
import QtQuick.Layouts

Rectangle {
    id: root

    required property int currentStep
    required property bool developerPreview

    color: "#1d1d1f"
    radius: 20

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 30
        spacing: 0

        RowLayout {
            spacing: 12

            Rectangle {
                Layout.preferredWidth: 38
                Layout.preferredHeight: 38
                radius: 19
                color: "#2c2c2e"
                border.width: 1
                border.color: "#48484a"

                Image {
                    anchors.fill: parent
                    anchors.margins: 3
                    source: "../../assets/installer-artwork.png"
                    fillMode: Image.PreserveAspectFit
                    smooth: true
                }
            }

            Text {
                text: "AmseokOS"
                color: "#f5f5f7"
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
                    color: stepRow.modelData.selected
                           ? "#0a84ff"
                           : (stepRow.modelData.reached ? "#3a3a3c" : "#2c2c2e")

                    Text {
                        anchors.centerIn: parent
                        text: stepRow.modelData.number
                        color: stepRow.modelData.reached ? "#ffffff" : "#8e8e93"
                        font.pixelSize: 13
                        font.bold: true
                    }
                }

                Text {
                    text: stepRow.modelData.label
                    color: stepRow.modelData.selected ? "#f5f5f7" : "#98989d"
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
            text: root.developerPreview
                  ? qsTr("开发者预览 · 使用模拟数据")
                  : qsTr("安全预览 · 不会修改磁盘")
            color: "#8e8e93"
            font.pixelSize: 12
            wrapMode: Text.WordWrap
        }
    }
}

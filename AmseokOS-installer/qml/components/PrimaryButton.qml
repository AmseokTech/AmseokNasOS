//--------------------------//
//--------提供安装器统一的主要操作按钮---------//
//--------Provides the installer's consistent primary action button--------//
//-------------------------//
import QtQuick
import QtQuick.Controls

Button {
    id: control

    hoverEnabled: true
    implicitWidth: 124
    implicitHeight: 40
    font.pixelSize: 14
    font.weight: Font.DemiBold

    contentItem: Text {
        text: control.text
        font: control.font
        color: control.enabled ? "#ffffff" : "#8e8e93"
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
    }

    background: Rectangle {
        radius: 8
        color: control.enabled
               ? (control.down ? "#006edb" : (control.hovered ? "#409cff" : "#0a84ff"))
               : "#3a3a3c"
        border.width: control.enabled ? 1 : 0
        border.color: "#5fb0ff"
    }
}

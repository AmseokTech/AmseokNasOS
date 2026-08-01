//--------------------------//
//--------提供安装器统一的主要操作按钮---------//
//--------Provides the installer's consistent primary action button--------//
//-------------------------//
import QtQuick
import QtQuick.Controls

Button {
    id: control

    implicitWidth: 132
    implicitHeight: 44
    font.pixelSize: 15
    font.weight: Font.DemiBold

    contentItem: Text {
        text: control.text
        font: control.font
        color: control.enabled ? "#ffffff" : "#98a2b3"
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
    }

    background: Rectangle {
        radius: 11
        color: control.enabled ? (control.down ? "#164c8c" : "#1f6fbd") : "#e4e7ec"
    }
}

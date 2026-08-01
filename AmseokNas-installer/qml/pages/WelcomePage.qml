//--------------------------//
//--------展示安装器主视觉并发出进入安装流程的意图---------//
//--------Shows the installer hero and emits intent to enter the install flow--------//
//-------------------------//
import QtQuick
import QtQuick.Layouts
import "../components"

Item {
    id: pageRoot

    required property var session

    signal installRequested

    ColumnLayout {
        anchors.centerIn: parent
        width: Math.min(parent.width - 96, 620)
        spacing: 0

        Image {
            Layout.alignment: Qt.AlignHCenter
            Layout.preferredWidth: 286
            Layout.preferredHeight: 286
            source: "../../assets/installer-artwork.png"
            fillMode: Image.PreserveAspectFit
            smooth: true
            mipmap: true
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            Layout.topMargin: 24
            text: qsTr("AmseokOS 安装程序")
            color: "#f5f5f7"
            font.pixelSize: 30
            font.weight: Font.DemiBold
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            Layout.topMargin: 18
            Layout.maximumWidth: 520
            horizontalAlignment: Text.AlignHCenter
            text: qsTr("若要设置并安装 AmseokOS，请点按“安装”。")
            color: "#d1d1d6"
            font.pixelSize: 15
            wrapMode: Text.WordWrap
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            Layout.topMargin: 8
            text: qsTr("Debian %1 · %2").arg(pageRoot.session.distribution).arg(pageRoot.session.architecture)
            color: "#8e8e93"
            font.pixelSize: 12
        }

        PrimaryButton {
            Layout.alignment: Qt.AlignHCenter
            Layout.topMargin: 28
            implicitWidth: 118
            text: qsTr("安装")
            enabled: true
            onClicked: pageRoot.installRequested()
        }
    }
}

//--------------------------//
//--------组装只读安装器预览并保持特权执行默认关闭---------//
//--------Composes the read-only installer preview with privileged execution
// disabled--------//
//-------------------------//
#include "adapters/disabled_installation_executor.h"
#include "presentation/installer_session.h"

#include <QCommandLineOption>
#include <QCommandLineParser>
#include <QGuiApplication>
#include <QQmlApplicationEngine>
#include <QQuickStyle>
#include <QUrl>
#include <QVariant>

int main(int argc, char* argv[]) {
  QGuiApplication application(argc, argv);
  QQuickStyle::setStyle(QStringLiteral("Basic"));
  QCoreApplication::setApplicationName("AmseokOS Installer");
  QCoreApplication::setApplicationVersion("0.1.0");

  QCommandLineParser parser;
  parser.setApplicationDescription("AmseokOS Debian image installer preview");
  parser.addHelpOption();
  parser.addVersionOption();
  const QCommandLineOption windowed_option(
      "windowed",
      "Run in a normal desktop window instead of installer full-screen mode");
  const QCommandLineOption smoke_test_option(
      "smoke-test",
      "Load the QML interface and exit without entering the event loop");
  parser.addOption(windowed_option);
  parser.addOption(smoke_test_option);
  parser.process(application);

  amseokos::installer::DisabledInstallationExecutor executor;
  amseokos::installer::InstallerSession session(executor);

  QQmlApplicationEngine engine;
  engine.setInitialProperties({
      {"installerSession",
       QVariant::fromValue(static_cast<QObject*>(&session))},
      {"windowedPreview", parser.isSet(windowed_option)},
  });
  engine.load(
      QUrl(QStringLiteral("qrc:/qt/qml/AmseokOS/Installer/qml/Main.qml")));

  if (engine.rootObjects().isEmpty()) {
    return 1;
  }

  if (parser.isSet(smoke_test_option)) {
    return 0;
  }

  return application.exec();
}

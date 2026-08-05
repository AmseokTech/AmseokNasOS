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
#ifdef AMSEOKOS_ENABLE_DEVELOPER_PREVIEW
  const QCommandLineOption developer_preview_option(
      "developer-preview",
      "Use simulated installer data for live QML development");
  parser.addOption(developer_preview_option);
#endif
  parser.process(application);

  amseokos::installer::DisabledInstallationExecutor executor;
  amseokos::installer::InstallerSession session(executor);

  QQmlApplicationEngine engine;
  QUrl entry_point(
      QStringLiteral("qrc:/qt/qml/AmseokOS/Installer/qml/Main.qml"));

#ifdef AMSEOKOS_ENABLE_DEVELOPER_PREVIEW
  if (parser.isSet(developer_preview_option)) {
    entry_point = QUrl(QStringLiteral(
        "qrc:/qt/qml/AmseokOS/Installer/qml/DeveloperPreview.qml"));
  } else {
#endif
    engine.setInitialProperties({
        {"installerSession",
         QVariant::fromValue(static_cast<QObject*>(&session))},
        {"windowedPreview", parser.isSet(windowed_option)},
    });
#ifdef AMSEOKOS_ENABLE_DEVELOPER_PREVIEW
  }
#endif

  engine.load(entry_point);

  if (engine.rootObjects().isEmpty()) {
    return 1;
  }

  if (parser.isSet(smoke_test_option)) {
    return 0;
  }

  return application.exec();
}

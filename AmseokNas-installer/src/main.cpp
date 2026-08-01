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
#include <QVariant>

int main(int argc, char* argv[]) {
  QGuiApplication application(argc, argv);
  QCoreApplication::setApplicationName("AmseokOS Installer");
  QCoreApplication::setApplicationVersion("0.1.0");

  QCommandLineParser parser;
  parser.setApplicationDescription("AmseokOS Debian image installer preview");
  parser.addHelpOption();
  parser.addVersionOption();
  const QCommandLineOption windowed_option(
      "windowed",
      "Run in a normal desktop window instead of installer full-screen mode");
  parser.addOption(windowed_option);
  parser.process(application);

  amseokos::installer::DisabledInstallationExecutor executor;
  amseokos::installer::InstallerSession session(executor);

  QQmlApplicationEngine engine;
  engine.setInitialProperties({
      {"installerSession",
       QVariant::fromValue(static_cast<QObject*>(&session))},
      {"windowedPreview", parser.isSet(windowed_option)},
  });
  engine.loadFromModule("AmseokOS.Installer", "Main");

  if (engine.rootObjects().isEmpty()) {
    return 1;
  }

  return application.exec();
}

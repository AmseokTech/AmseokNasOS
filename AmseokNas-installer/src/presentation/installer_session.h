//--------------------------//
//--------向 QML 暴露唯一的安装会话状态与导航入口---------//
//--------Exposes the sole installer session state and navigation entry point to
// QML--------//
//-------------------------//
#pragma once

#include "domain/installation_plan.h"
#include "ports/installation_executor.h"

#include <QObject>
#include <QString>

namespace amseokos::installer {

class InstallerSession final : public QObject {
  Q_OBJECT
  Q_PROPERTY(int currentStep READ current_step NOTIFY currentStepChanged)
  Q_PROPERTY(bool canGoBack READ can_go_back NOTIFY currentStepChanged)
  Q_PROPERTY(bool canGoForward READ can_go_forward NOTIFY currentStepChanged)
  Q_PROPERTY(
      bool canStartInstallation READ can_start_installation NOTIFY planChanged)
  Q_PROPERTY(bool executionEnabled READ execution_enabled CONSTANT)
  Q_PROPERTY(QString distribution READ distribution CONSTANT)
  Q_PROPERTY(QString architecture READ architecture CONSTANT)
  Q_PROPERTY(
      QString validationMessage READ validation_message NOTIFY planChanged)
  Q_PROPERTY(
      QString statusMessage READ status_message NOTIFY statusMessageChanged)

public:
  explicit InstallerSession(IInstallationExecutor& executor,
                            QObject* parent = nullptr);

  [[nodiscard]] int current_step() const;
  [[nodiscard]] bool can_go_back() const;
  [[nodiscard]] bool can_go_forward() const;
  [[nodiscard]] bool can_start_installation() const;
  [[nodiscard]] bool execution_enabled() const;
  [[nodiscard]] QString distribution() const;
  [[nodiscard]] QString architecture() const;
  [[nodiscard]] QString validation_message() const;
  [[nodiscard]] QString status_message() const;

  Q_INVOKABLE void goBack();
  Q_INVOKABLE void goForward();
  Q_INVOKABLE void startInstallation();

signals:
  void currentStepChanged();
  void planChanged();
  void statusMessageChanged();

private:
  void set_status_message(QString message);

  static constexpr int kLastStep = 2;

  IInstallationExecutor& executor_;
  InstallationPlan plan_;
  int current_step_ = 0;
  QString status_message_;
};

} // namespace amseokos::installer

//--------------------------//
//--------编排安全预览流程且不在界面层执行系统命令---------//
//--------Orchestrates the safe preview flow without system commands in the UI
// layer--------//
//-------------------------//
#include "presentation/installer_session.h"

#include <utility>

namespace amseokos::installer {

InstallerSession::InstallerSession(IInstallationExecutor& executor,
                                   QObject* parent)
    : QObject(parent), executor_(executor) {}

int InstallerSession::current_step() const { return current_step_; }

bool InstallerSession::can_go_back() const { return current_step_ > 0; }

bool InstallerSession::can_go_forward() const {
  return current_step_ < kLastStep;
}

bool InstallerSession::can_start_installation() const {
  return plan_.validate().valid && executor_.is_available();
}

bool InstallerSession::execution_enabled() const {
  return executor_.is_available();
}

QString InstallerSession::distribution() const {
  return QString::fromStdString(plan_.distribution);
}

QString InstallerSession::architecture() const {
  return QString::fromStdString(plan_.architecture);
}

QString InstallerSession::validation_message() const {
  const auto validation = plan_.validate();
  return validation.valid ? QString{}
                          : QString::fromStdString(validation.message);
}

QString InstallerSession::status_message() const { return status_message_; }

void InstallerSession::goBack() {
  if (!can_go_back()) {
    return;
  }

  --current_step_;
  emit currentStepChanged();
}

void InstallerSession::goForward() {
  if (!can_go_forward()) {
    return;
  }

  ++current_step_;
  emit currentStepChanged();
}

void InstallerSession::startInstallation() {
  const auto validation = plan_.validate();
  if (!validation.valid) {
    set_status_message(QString::fromStdString(validation.message));
    return;
  }

  const auto result = executor_.execute(plan_);
  set_status_message(QString::fromStdString(result.message));
}

void InstallerSession::set_status_message(QString message) {
  if (status_message_ == message) {
    return;
  }

  status_message_ = std::move(message);
  emit statusMessageChanged();
}

} // namespace amseokos::installer

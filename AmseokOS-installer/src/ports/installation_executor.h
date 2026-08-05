//--------------------------//
//--------声明安装编排可调用的唯一特权执行端口---------//
//--------Declares the sole privileged execution port used by
// orchestration--------//
//-------------------------//
#pragma once

#include "domain/installation_plan.h"

#include <string>

namespace amseokos::installer {

struct ExecutionResult {
  bool accepted;
  std::string code;
  std::string message;
};

class IInstallationExecutor {
public:
  virtual ~IInstallationExecutor() = default;

  [[nodiscard]] virtual bool is_available() const = 0;
  [[nodiscard]] virtual ExecutionResult
  execute(const InstallationPlan& plan) = 0;
};

} // namespace amseokos::installer

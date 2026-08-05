//--------------------------//
//--------拒绝所有安装请求直到特权执行链完成安全验收---------//
//--------Rejects all installs until the privileged execution path is
// accepted--------//
//-------------------------//
#include "adapters/disabled_installation_executor.h"

namespace amseokos::installer {

bool DisabledInstallationExecutor::is_available() const { return false; }

ExecutionResult DisabledInstallationExecutor::execute(const InstallationPlan&) {
  return {
      false,
      "execution.disabled",
      "Installation execution is disabled in the architecture scaffold",
  };
}

} // namespace amseokos::installer

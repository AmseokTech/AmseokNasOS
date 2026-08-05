//--------------------------//
//--------提供默认关闭且不会触碰磁盘的执行适配器---------//
//--------Provides the default disabled adapter that never touches
// disks--------//
//-------------------------//
#pragma once

#include "ports/installation_executor.h"

namespace amseokos::installer {

class DisabledInstallationExecutor final : public IInstallationExecutor {
public:
  [[nodiscard]] bool is_available() const override;
  [[nodiscard]] ExecutionResult execute(const InstallationPlan& plan) override;
};

} // namespace amseokos::installer

//--------------------------//
//--------集中校验安装目标、发行版基线与数据盘保护---------//
//--------Centralizes target, distribution, and data-disk protection
// validation--------//
//-------------------------//
#include "domain/installation_plan.h"

namespace amseokos::installer {

PlanValidationResult InstallationPlan::validate() const {
  if (distribution != "trixie" || architecture != "amd64") {
    return {false, "plan.unsupported_base",
            "Only Debian trixie amd64 is supported"};
  }

  if (filesystem != "ext4") {
    return {false, "plan.unsupported_filesystem",
            "Only ext4 is supported for the system disk"};
  }

  if (!preserve_non_target_disks) {
    return {false, "plan.data_disk_protection_required",
            "Non-target disks must remain untouched"};
  }

  if (!system_disk.has_value()) {
    return {false, "plan.system_disk_required",
            "Select a system disk before installation"};
  }

  if (system_disk->stable_id.empty() || system_disk->display_name.empty() ||
      system_disk->size_bytes == 0) {
    return {false, "plan.invalid_system_disk",
            "The system disk requires a stable identity"};
  }

  if (!destructive_action_confirmed) {
    return {false, "plan.confirmation_required",
            "Confirm the destructive system-disk action"};
  }

  return {true, "", ""};
}

} // namespace amseokos::installer

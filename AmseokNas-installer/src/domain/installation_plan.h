//--------------------------//
//--------定义安装计划与不可越过的磁盘安全条件---------//
//--------Defines installation plans and non-bypassable disk safety
// conditions--------//
//-------------------------//
#pragma once

#include <cstdint>
#include <optional>
#include <string>

namespace amseokos::installer {

struct SystemDiskTarget {
  std::string stable_id;
  std::string display_name;
  std::uint64_t size_bytes = 0;
};

struct PlanValidationResult {
  bool valid;
  std::string code;
  std::string message;
};

struct InstallationPlan {
  std::string distribution = "trixie";
  std::string architecture = "amd64";
  std::string filesystem = "ext4";
  std::optional<SystemDiskTarget> system_disk;
  bool destructive_action_confirmed = false;
  bool preserve_non_target_disks = true;

  [[nodiscard]] PlanValidationResult validate() const;
};

} // namespace amseokos::installer

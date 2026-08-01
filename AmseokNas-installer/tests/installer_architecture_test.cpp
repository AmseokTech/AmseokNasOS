//--------------------------//
//--------通过公共契约验证安装计划与执行边界---------//
//--------Verifies installation planning and execution boundaries through public
// contracts--------//
//-------------------------//
#include "adapters/disabled_installation_executor.h"
#include "domain/installation_plan.h"
#include "ports/installation_executor.h"
#include "presentation/installer_session.h"

#include <QSignalSpy>
#include <QtTest>

namespace {

class RecordingExecutor final
    : public amseokos::installer::IInstallationExecutor {
public:
  bool is_available() const override { return true; }

  amseokos::installer::ExecutionResult
  execute(const amseokos::installer::InstallationPlan&) override {
    ++execution_count;
    return {true, "", "accepted"};
  }

  int execution_count = 0;
};

class InstallerArchitectureTest final : public QObject {
  Q_OBJECT

private slots:
  void default_plan_requires_an_explicit_system_disk() {
    const amseokos::installer::InstallationPlan plan;

    const auto result = plan.validate();

    QVERIFY(!result.valid);
    QCOMPARE(result.code, std::string("plan.system_disk_required"));
  }

  void valid_plan_uses_a_stable_disk_identity_and_preserves_other_disks() {
    amseokos::installer::InstallationPlan plan;
    plan.system_disk = amseokos::installer::SystemDiskTarget{
        "wwn-0x5000c50000000001",
        "Example SSD 256 GB",
        256'000'000'000,
    };
    plan.destructive_action_confirmed = true;

    const auto result = plan.validate();

    QVERIFY(result.valid);
  }

  void plan_rejects_any_request_to_touch_non_target_disks() {
    amseokos::installer::InstallationPlan plan;
    plan.system_disk = amseokos::installer::SystemDiskTarget{
        "wwn-0x5000c50000000001",
        "Example SSD 256 GB",
        256'000'000'000,
    };
    plan.destructive_action_confirmed = true;
    plan.preserve_non_target_disks = false;

    const auto result = plan.validate();

    QVERIFY(!result.valid);
    QCOMPARE(result.code, std::string("plan.data_disk_protection_required"));
  }

  void session_does_not_call_the_executor_for_an_invalid_plan() {
    RecordingExecutor executor;
    amseokos::installer::InstallerSession session(executor);
    QSignalSpy status_spy(
        &session, &amseokos::installer::InstallerSession::statusMessageChanged);

    session.startInstallation();

    QCOMPARE(executor.execution_count, 0);
    QCOMPARE(status_spy.count(), 1);
    QVERIFY(!session.status_message().isEmpty());
  }

  void disabled_adapter_rejects_execution_without_side_effects() {
    amseokos::installer::DisabledInstallationExecutor executor;
    const amseokos::installer::InstallationPlan plan;

    const auto result = executor.execute(plan);

    QVERIFY(!executor.is_available());
    QVERIFY(!result.accepted);
    QCOMPARE(result.code, std::string("execution.disabled"));
  }

  void production_session_never_exposes_developer_mock_data() {
    amseokos::installer::DisabledInstallationExecutor executor;
    amseokos::installer::InstallerSession session(executor);

    QVERIFY(!session.developer_preview());
    QVERIFY(!session.has_system_disk());
    QVERIFY(session.system_disk_display_name().isEmpty());
    QVERIFY(session.system_disk_stable_id().isEmpty());
    QVERIFY(session.system_disk_capacity().isEmpty());
  }

  void navigation_stays_inside_the_declared_steps() {
    RecordingExecutor executor;
    amseokos::installer::InstallerSession session(executor);

    session.goBack();
    QCOMPARE(session.current_step(), 0);

    session.goForward();
    session.goForward();
    session.goForward();
    QCOMPARE(session.current_step(), 2);

    session.goBack();
    QCOMPARE(session.current_step(), 1);
  }
};

} // namespace

QTEST_APPLESS_MAIN(InstallerArchitectureTest)

#include "installer_architecture_test.moc"

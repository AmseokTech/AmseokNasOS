cmake_minimum_required(VERSION 3.21)

project(AmseokOSInstaller VERSION 0.1.0 LANGUAGES CXX)

option(AMSEOKOS_ENABLE_DEVELOPER_PREVIEW
    "Build the isolated live QML developer preview"
    OFF
)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Qt6 6.4 REQUIRED COMPONENTS Core Quick QuickControls2 Test)

qt_standard_project_setup(REQUIRES 6.4)

if(Qt6_VERSION VERSION_GREATER_EQUAL 6.8)
    qt_policy(SET QTP0004 NEW)
endif()

function(amseokos_enable_strict_warnings target_name)
    if(CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang|GNU")
        target_compile_options(${target_name} PRIVATE -Wall -Wextra -Wpedantic -Werror)
    elseif(MSVC)
        target_compile_options(${target_name} PRIVATE /W4 /WX)
    endif()
endfunction()

add_library(amseokos-installer-core STATIC
    src/adapters/disabled_installation_executor.cpp
    src/adapters/disabled_installation_executor.h
    src/domain/installation_plan.cpp
    src/domain/installation_plan.h
    src/ports/installation_executor.h
    src/presentation/installer_session.cpp
    src/presentation/installer_session.h
)

target_include_directories(amseokos-installer-core PUBLIC src)
target_link_libraries(amseokos-installer-core PUBLIC Qt6::Core)
amseokos_enable_strict_warnings(amseokos-installer-core)

qt_add_executable(amseokos-installer
    src/main.cpp
)

set_target_properties(amseokos-installer PROPERTIES MACOSX_BUNDLE TRUE)

set(amseokos_installer_qml_files
    qml/Main.qml
    qml/components/PrimaryButton.qml
    qml/components/StepRail.qml
    qml/pages/ReviewPage.qml
    qml/pages/SystemDiskPage.qml
    qml/pages/WelcomePage.qml
)

if(AMSEOKOS_ENABLE_DEVELOPER_PREVIEW)
    list(APPEND amseokos_installer_qml_files qml/DeveloperPreview.qml)
endif()

qt_add_qml_module(amseokos-installer
    URI AmseokOS.Installer
    VERSION 1.0
    RESOURCE_PREFIX /qt/qml
    QML_FILES ${amseokos_installer_qml_files}
    RESOURCES
        assets/installer-artwork.png
)

target_link_libraries(amseokos-installer PRIVATE
    amseokos-installer-core
    Qt6::Quick
    Qt6::QuickControls2
)
amseokos_enable_strict_warnings(amseokos-installer)

if(AMSEOKOS_ENABLE_DEVELOPER_PREVIEW)
    target_compile_definitions(amseokos-installer PRIVATE
        AMSEOKOS_ENABLE_DEVELOPER_PREVIEW
        QT_QML_DEBUG
    )

    get_target_property(qt_qmake_executable Qt6::qmake IMPORTED_LOCATION)
    get_filename_component(qt_binary_directory "${qt_qmake_executable}" DIRECTORY)
    find_program(amseokos_qmlpreview_executable
        NAMES qmlpreview qmlpreview6
        HINTS "${qt_binary_directory}"
    )

    if(amseokos_qmlpreview_executable)
        add_custom_target(developer-preview
            COMMAND "${amseokos_qmlpreview_executable}"
                "$<TARGET_FILE:amseokos-installer>"
                --developer-preview
            DEPENDS amseokos-installer
            WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
            USES_TERMINAL
            VERBATIM
        )
    else()
        message(STATUS
            "qmlpreview was not found; preview builds remain available, but live reload requires Qt QML tooling"
        )
        add_custom_target(developer-preview
            COMMAND "${CMAKE_COMMAND}" -E echo
                "qmlpreview is required for live reload; install Qt QML tooling or add qmlpreview to PATH"
            COMMAND "${CMAKE_COMMAND}" -E false
            USES_TERMINAL
            VERBATIM
        )
    endif()
endif()

install(TARGETS amseokos-installer
    BUNDLE DESTINATION .
    RUNTIME DESTINATION bin
)

include(CTest)

if(BUILD_TESTING)
    qt_add_executable(amseokos-installer-tests
        tests/installer_architecture_test.cpp
    )

    target_link_libraries(amseokos-installer-tests PRIVATE
        amseokos-installer-core
        Qt6::Test
    )
    amseokos_enable_strict_warnings(amseokos-installer-tests)

    add_test(NAME amseokos-installer-tests COMMAND amseokos-installer-tests)

    if(AMSEOKOS_ENABLE_DEVELOPER_PREVIEW)
        add_test(
            NAME amseokos-installer-developer-preview-smoke
            COMMAND amseokos-installer --developer-preview --smoke-test
        )
        set_tests_properties(
            amseokos-installer-developer-preview-smoke
            PROPERTIES ENVIRONMENT "QT_QPA_PLATFORM=offscreen"
        )
    endif()
endif()

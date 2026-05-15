cmake_minimum_required(VERSION 3.20)
project(ax25sdl C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

# Treat warnings strictly. Generated code goes through the same flag
# set as hand-written, so any emitter slip-up (unused static, missing
# field initialiser) lights up at codegen time rather than in CI.
if(CMAKE_C_COMPILER_ID MATCHES "GNU|Clang")
    add_compile_options(-Wall -Wextra -Wpedantic -Werror -Wno-unused-function)
endif()

# Library: every .c under src/. The generated .g.c files define
# `const` page instances; their static-storage support arrays don't
# need separate compilation.
file(GLOB SDL_SOURCES CONFIGURE_DEPENDS src/*.c)
add_library(ax25sdl ${SDL_SOURCES})
target_include_directories(ax25sdl PUBLIC include src)

enable_testing()

# Each test source is a standalone executable with its own main().
# CTest picks up the executable by its filename stem.
file(GLOB TEST_SOURCES CONFIGURE_DEPENDS test/*.c)
foreach(test_src ${TEST_SOURCES})
    get_filename_component(test_name ${test_src} NAME_WE)
    add_executable(${test_name} ${test_src})
    target_link_libraries(${test_name} PRIVATE ax25sdl)
    add_test(NAME ${test_name} COMMAND ${test_name})
endforeach()

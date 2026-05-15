// SDL runtime types — hand-written.
//
// The .g.c files under c-spec/src/ define `const` values of these
// types. Empty-string sentinels (`""`) stand in for absent strings,
// and `0` stands in for absent line numbers — matching the Go and C#
// conventions so generated initialisers stay readable. C lacks slice
// types, so every variable-length list is encoded as a paired
// `const T*` + `size_t T_len` field.
#ifndef AX25SDL_H
#define AX25SDL_H

#include <stddef.h>

typedef enum {
    AX25SDL_KIND_SIGNAL_UPPER,
    AX25SDL_KIND_SIGNAL_LOWER,
    AX25SDL_KIND_PROCESSING,
    AX25SDL_KIND_SUBROUTINE,
    AX25SDL_KIND_INTERNAL_OUT
} ActionKind;

typedef struct {
    const char* spec;
    const char* figure;
    const char* url;
} SdlSource;

typedef struct {
    const char* verb;
    ActionKind kind;
} ActionStep;

typedef struct {
    size_t start;
    size_t length;
    const char* predicate;
} LoopRange;

typedef struct {
    const char* source;
    const char* cite;
    const char* quote;
    const char* path;
    const char* function;
    unsigned int line;  // 0 = no line citation
    const char* note;
} ImplementationReference;

typedef struct {
    const char* id;
    const char* from;
    const char* on;
    const char* guard;
    const ActionStep* actions;
    size_t actions_len;
    const char* next;
    const char* notes;
    const ImplementationReference* references;
    size_t references_len;
    const LoopRange* loops;
    size_t loops_len;
} TransitionSpec;

typedef struct {
    const char* id;
    const char* guard;
    const ActionStep* actions;
    size_t actions_len;
    const char* notes;
    const ImplementationReference* references;
    size_t references_len;
    const LoopRange* loops;
    size_t loops_len;
} SubroutinePath;

typedef struct {
    const char* name;
    const SubroutinePath* paths;
    size_t paths_len;
    const char* notes;
    const ImplementationReference* references;
    size_t references_len;
} SubroutineSpec;

typedef struct {
    const char* machine;
    const char* state;
    SdlSource source;
    const TransitionSpec* transitions;
    size_t transitions_len;
} StatePage;

typedef struct {
    const char* machine;
    SdlSource source;
    const SubroutineSpec* subroutines;
    size_t subroutines_len;
} SubroutinesPage;

#endif  // AX25SDL_H

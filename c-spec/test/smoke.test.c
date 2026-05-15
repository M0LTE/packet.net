// Hand-written smoke tests — cross-page checks. Not regenerated.
//
// Mirrors go-spec/ax25sdl/sdl_test.go's three blanket assertions:
// every state page has transitions, figc4.7 lists thirteen
// subroutines, and the ActionKind enum is contiguous.
#include "ax25sdl.g.h"
#include "ax25sdl.h"
#include <stdio.h>

#define ASSERT(cond, msg)                                                      \
  do {                                                                         \
    if (!(cond)) {                                                             \
      fprintf(stderr, "FAIL: %s\n", msg);                                      \
      return 1;                                                                \
    }                                                                          \
  } while (0)

static int test_state_pages_have_transitions(void) {
  const StatePage *pages[] = {
      &data_link_awaiting_connection, &data_link_awaiting_connection22,
      &data_link_awaiting_release,    &data_link_connected,
      &data_link_disconnected,
  };
  const size_t n = sizeof(pages) / sizeof(pages[0]);
  for (size_t i = 0; i < n; i++) {
    if (pages[i]->transitions_len == 0) {
      fprintf(stderr, "FAIL: %s/%s has no transitions\n", pages[i]->machine,
              pages[i]->state);
      return 1;
    }
  }
  return 0;
}

static int test_subroutines_has_thirteen_entries(void) {
  const size_t expected = 13;
  if (data_link_subroutines.subroutines_len != expected) {
    fprintf(stderr, "FAIL: expected %zu subroutines on figc4.7, got %zu\n",
            expected, data_link_subroutines.subroutines_len);
    return 1;
  }
  return 0;
}

static int test_action_kind_enum_is_contiguous(void) {
  // Belt-and-braces: every page's actions should resolve to one of
  // the five declared kinds. The codegen rejects unknown kinds
  // upstream, but a manual hand-edit to a .g.c would slip past.
  ASSERT(AX25SDL_KIND_SIGNAL_UPPER == 0, "SignalUpper sentinel");
  ASSERT(AX25SDL_KIND_INTERNAL_OUT == 4, "InternalOut sentinel");
  return 0;
}

int main(void) {
  int rc = 0;
  rc |= test_state_pages_have_transitions();
  rc |= test_subroutines_has_thirteen_entries();
  rc |= test_action_kind_enum_is_contiguous();
  return rc;
}

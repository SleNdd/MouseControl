#include <Mouse.h>
#include "Protocol.h"

// Only validated host MOVE commands cause movement. No buttons, wheel, or autonomous motion.
static MouseControlProtocol protocol;
static uint8_t incoming[MC_FRAME_LENGTH];
static uint8_t received = 0;
static unsigned long lastByteAt = 0;

static void resetConnection() {
  protocol.reset();
  received = 0;
}

void setup() {
  Serial.begin(115200);
  Mouse.begin();
}

void loop() {
  // Leonardo's native USB CDC DTR marks the port open; avoid Serial's delayed bool conversion.
  if (!Serial.dtr()) {
    resetConnection();
    for (uint8_t i = 0; i < 64 && Serial.available() > 0; ++i) Serial.read();
    return;
  }
  if (received != 0 && (unsigned long)(millis() - lastByteAt) > 250) received = 0;
  // Process at most one whole command per iteration. Host waits for its ACK before writing another.
  uint8_t byteBudget = 64;
  while (byteBudget-- != 0 && Serial.available() > 0) {
    const uint8_t value = (uint8_t)Serial.read();
    lastByteAt = millis();
    if (received == 0) {
      if (value == 0xA5) incoming[received++] = value;
      continue;
    }
    if (received == 1 && value != 0x5A) {
      received = value == 0xA5 ? 1 : 0;
      continue;
    }
    incoming[received++] = value;
    if (received == MC_FRAME_LENGTH) {
      received = 0;
      uint8_t response[MC_FRAME_LENGTH];
      int8_t dx = 0, dy = 0;
      bool move = false;
      if (protocol.accept(incoming, response, move, dx, dy)) {
        if (move && Serial.dtr()) Mouse.move(dx, dy, 0);
        if (Serial.dtr()) Serial.write(response, MC_FRAME_LENGTH);
      }
      return;
    }
  }
}

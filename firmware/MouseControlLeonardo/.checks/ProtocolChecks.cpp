#include "../Protocol.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

static void frame(uint8_t* bytes, uint8_t command, uint32_t session, uint32_t sequence, int8_t dx = 0, int8_t dy = 0) {
  memset(bytes, 0, MC_FRAME_LENGTH);
  bytes[0] = 0xA5; bytes[1] = 0x5A; bytes[2] = MC_VERSION; bytes[3] = command;
  mcWrite32(bytes + 4, session); mcWrite32(bytes + 8, sequence);
  bytes[12] = (uint8_t)dx; bytes[13] = (uint8_t)dy;
  const uint16_t crc = mcCrc(bytes, 14);
  bytes[14] = (uint8_t)crc; bytes[15] = (uint8_t)(crc >> 8);
}

static void replyIs(const uint8_t* reply, uint8_t command, uint8_t code = 0) {
  assert(reply[3] == command && reply[12] == code);
  assert(((uint16_t)reply[14] | ((uint16_t)reply[15] << 8)) == mcCrc(reply, 14));
}

int main() {
  const uint8_t golden[] = {'1','2','3','4','5','6','7','8','9'};
  assert(mcCrc(golden, 9) == 0x29B1);
  MouseControlProtocol protocol;
  uint8_t request[MC_FRAME_LENGTH], reply[MC_FRAME_LENGTH];
  bool move = false;
  int8_t dx = 0, dy = 0;
  const uint8_t helloGolden[MC_FRAME_LENGTH] = {0xA5,0x5A,0x01,0x01,0x04,0x03,0x02,0x01,0,0,0,0,0,0,0x6D,0x44};
  frame(request, MC_HELLO, 0x01020304, 0);
  assert(memcmp(request, helloGolden, MC_FRAME_LENGTH) == 0);
  frame(request, MC_MOVE, 10, 1, 1, 1);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 5);
  frame(request, MC_HELLO, 10, 0);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_HELLO_ACK, 'M');
  assert(reply[13] == 'C' && mcRead32(reply + 4) == 10 && mcRead32(reply + 8) == 0);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 1); // Replaying HELLO cannot reset the sequence.
  frame(request, MC_MOVE, 10, 1, -127, 127);
  assert(protocol.accept(request, reply, move, dx, dy) && move && dx == -127 && dy == 127);
  replyIs(reply, MC_ACK);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 2); // Duplicate MOVE does not repeat.
  frame(request, MC_MOVE, 10, 3, 1, 1);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 2); // Skipped sequence is rejected.
  frame(request, MC_MOVE, 11, 2, 1, 1);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 5);
  frame(request, MC_MOVE, 10, 2, -128, 1);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 3);
  frame(request, 99, 10, 2);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 4);
  frame(request, MC_PING, 10, 2, 1, 0);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 3);
  frame(request, MC_PING, 10, 2);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_ACK);
  frame(request, MC_STOP, 10, 3);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_ACK);
  frame(request, MC_MOVE, 10, 2, 1, 1);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 2);
  protocol.reset();
  frame(request, MC_HELLO, 20, 0);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_HELLO_ACK, 'M');
  frame(request, MC_MOVE, 10, 1, 1, 1);
  assert(protocol.accept(request, reply, move, dx, dy) && !move);
  replyIs(reply, MC_NACK, 5);
  // Every single-byte corruption is rejected before connection/movement state changes.
  for (uint8_t i = 0; i < MC_FRAME_LENGTH; ++i) {
    protocol.reset();
    frame(request, MC_HELLO, 30, 0);
    request[i] ^= 1;
    assert(!protocol.accept(request, reply, move, dx, dy) && !move);
    frame(request, MC_HELLO, 30, 0);
    assert(protocol.accept(request, reply, move, dx, dy));
    replyIs(reply, MC_HELLO_ACK, 'M');
  }
  puts("PASS firmware protocol: CRC, identity, range, sequence, replay, stop, reconnect isolation");
  return 0;
}

#ifndef MOUSECONTROL_PROTOCOL_H
#define MOUSECONTROL_PROTOCOL_H

#include <stdint.h>

static const uint8_t MC_FRAME_LENGTH = 16;
static const uint8_t MC_VERSION = 1;
enum MouseControlCommand { MC_HELLO = 1, MC_PING = 2, MC_MOVE = 3, MC_STOP = 4,
                          MC_HELLO_ACK = 0x81, MC_ACK = 0x82, MC_NACK = 0x83 };

static uint16_t mcCrc(const uint8_t* bytes, uint8_t length) {
  uint16_t crc = 0xFFFF;
  for (uint8_t i = 0; i < length; ++i) {
    crc ^= (uint16_t)bytes[i] << 8;
    for (uint8_t bit = 0; bit < 8; ++bit)
      crc = (crc & 0x8000) ? (uint16_t)((crc << 1) ^ 0x1021) : (uint16_t)(crc << 1);
  }
  return crc;
}

static uint32_t mcRead32(const uint8_t* bytes) {
  return (uint32_t)bytes[0] | ((uint32_t)bytes[1] << 8) | ((uint32_t)bytes[2] << 16) | ((uint32_t)bytes[3] << 24);
}
static void mcWrite32(uint8_t* bytes, uint32_t value) {
  for (uint8_t i = 0; i < 4; ++i) bytes[i] = (uint8_t)(value >> (i * 8));
}

class MouseControlProtocol {
public:
  MouseControlProtocol() : connected(false), session(0), sequence(0) {}
  void reset() { connected = false; session = sequence = 0; }

  // CRC/version failures are silent. Valid rejected commands receive NACK without movement.
  bool accept(const uint8_t* frame, uint8_t* response, bool& move, int8_t& dx, int8_t& dy) {
    move = false;
    dx = dy = 0;
    if (frame[0] != 0xA5 || frame[1] != 0x5A || frame[2] != MC_VERSION ||
        ((uint16_t)frame[14] | ((uint16_t)frame[15] << 8)) != mcCrc(frame, 14)) return false;
    const uint8_t command = frame[3];
    const uint32_t incomingSession = mcRead32(frame + 4);
    const uint32_t incomingSequence = mcRead32(frame + 8);
    uint8_t reply = MC_ACK, error = 0;
    if (command == MC_HELLO) {
      if (connected || incomingSession == 0 || incomingSequence != 0 || frame[12] != 0 || frame[13] != 0) error = 1;
      else { connected = true; session = incomingSession; sequence = 0; reply = MC_HELLO_ACK; }
    } else if (!connected || incomingSession != session) error = 5;
    else if (sequence == UINT32_MAX || incomingSequence != sequence + 1) error = 2;
    else if (command != MC_PING && command != MC_MOVE && command != MC_STOP) error = 4;
    else if ((command != MC_MOVE && (frame[12] != 0 || frame[13] != 0)) ||
             frame[12] == 0x80 || frame[13] == 0x80) error = 3; // -128 is outside Mouse.move's allowed symmetric bound.
    else {
      sequence = incomingSequence;
      if (command == MC_MOVE) { dx = (int8_t)frame[12]; dy = (int8_t)frame[13]; move = true; }
    }
    response[0] = 0xA5; response[1] = 0x5A; response[2] = MC_VERSION;
    response[3] = error ? MC_NACK : reply;
    mcWrite32(response + 4, incomingSession);
    mcWrite32(response + 8, incomingSequence);
    response[12] = error ? error : (reply == MC_HELLO_ACK ? 'M' : 0);
    response[13] = reply == MC_HELLO_ACK && !error ? 'C' : 0;
    const uint16_t crc = mcCrc(response, 14);
    response[14] = (uint8_t)crc; response[15] = (uint8_t)(crc >> 8);
    return true;
  }

private:
  bool connected;
  uint32_t session, sequence;
};

#endif

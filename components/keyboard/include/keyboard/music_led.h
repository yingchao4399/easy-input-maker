#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace ai_keyboard {

// A bounded five-pixel bar. Values are RGB intensities, never power-domain
// controls; zero means the normal status background owns the strip again.
inline std::array<std::uint8_t, 5> music_led_levels(std::uint8_t level) {
  std::array<std::uint8_t, 5> pixels{};
  for (std::size_t index = 0; index < pixels.size(); ++index) {
    const auto threshold = static_cast<std::uint16_t>(index * 51U);
    const auto above = level > threshold ? level - threshold : 0U;
    pixels[index] = static_cast<std::uint8_t>(above > 51U ? 51U : above);
  }
  return pixels;
}

}  // namespace ai_keyboard

#pragma once
#include "pch.h"
#include <mutex>
#include <kissfft/kiss_fft.h>
namespace MusicPlayerLibrary
{
	struct AudioFrameData {
		std::vector<uint8_t> data;
		int samples;
	};

	// 飞8分钱干飞马
	// 干的飞马笑哈哈
	// 飞8分钱惊坐起
	// 飞马已经8bc
	class FFTExecuter
	{
		static constexpr size_t BYTES_PER_FRAME = 4; // 16bit * 2ch
		static constexpr size_t FFT_SIZE = 2048;     // fixed fft size
		static constexpr size_t RING_BUFFER_MAX_SIZE = FFT_SIZE * BYTES_PER_FRAME;

		void AddSamplesToRingBuffer(uint8_t* samples, int sample_size);
		[[nodiscard]] int GetRingBufferSize() const;

		// apply window to ring buffer, convert to vector
		static void ApplyWindow(const std::vector<uint8_t>& input, std::vector<double>& output);
		static void DoFFT(const std::vector<double>& windowed_data, std::vector<float>&, kiss_fft_cfg fft_cfg);
		static std::vector<size_t> GenBoundaries(float sample_rate, size_t fft_size, size_t segment_num, float f_lo = 20.0f, float f_hi = 20000.0f);
		static void MapFreqToSegments(std::vector<float>&, std::vector<float>&, const std::vector<size_t>&);

		void ExecuteAudioFFT();
		const std::vector<float> GetAudioFFTData();

		FFTExecuter(int sample_rate);
		~FFTExecuter();

	protected:
		// ring buffer, fix size=2560 samples
		std::deque<uint8_t> spectrum_data_ring_buffer;
		std::vector<float> spectrum_data;
		std::vector<float> spectrum_max_data{};
		std::vector<float> spectrum_smooth_data{};
		mutable std::mutex ring_buffer_mutex;

		// avoid duplicate allocation
		kiss_fft_cfg fft_cfg = nullptr;
		static constexpr size_t fft_size = FFT_SIZE;
		int sample_rate = 0;
		std::vector<kiss_fft_cpx> fft_in;
		std::vector<kiss_fft_cpx> fft_out;
	};

}
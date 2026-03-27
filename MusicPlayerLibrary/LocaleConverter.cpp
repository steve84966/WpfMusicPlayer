#include "pch.h"
#include "LocaleConverter.h"

#include <iconv.h>
#include <uchardet/uchardet.h>
#include <msclr/marshal_cppstd.h>
#include <vcclr.h>

CStringA MusicPlayerLibrary::LocaleConverterNative::GetUtf8StringFromBytesNative(const char* input, size_t size)
{
    std::vector<char> inbuf(input, input + size);
    auto uc_checker = uchardet_new();
    int  result = uchardet_handle_data(uc_checker, inbuf.data(), inbuf.size());
    uchardet_data_end(uc_checker);
    const char* charset = uchardet_get_charset(uc_checker);
    ATLTRACE("info: detected charset = %s\n", charset);
    CStringA outbuf; // place temporary UTF-8 String here
    size_t insize = size, outsize = size * 2;
    char* _pindata = (char*)inbuf.data();
    char* _poutdata = (char*)outbuf.GetBufferSetLength(outsize);
    iconv_t iconver = iconv_open("utf-8", charset);
    int  bytes = iconv(iconver, &_pindata, &insize, &_poutdata, &outsize);
    outbuf.ReleaseBuffer();
    if (bytes)
    {
        return CStringA(input, size);
    }
    else
    {
        return outbuf;
    }
}

CString MusicPlayerLibrary::LocaleConverterNative::GetUtf16StringFromUtf8String(const CStringA& input)
{
    int wide_len = MultiByteToWideChar(CP_UTF8, 0, input, -1, nullptr, 0);
    CString file_content_w;
    MultiByteToWideChar(CP_UTF8, 0, input, -1, file_content_w.GetBuffer(wide_len), wide_len);
    file_content_w.ReleaseBuffer();
    return file_content_w;
}

System::String^ MusicPlayerLibrary::LocaleConverter::GetSystemStringFromBytes(array<byte>^ input)
{
    if (input == nullptr || input->Length == 0)
        return gcnew System::String(L"");

    std::vector<char> buffer(input->Length);
    for (int i = 0; i < input->Length; ++i)
        buffer[i] = input[i];

    CStringA utf8 = LocaleConverterNative::GetUtf8StringFromBytesNative(buffer.data(), buffer.size());
    CString s = LocaleConverterNative::GetUtf16StringFromUtf8String(utf8);

    while (!s.IsEmpty())
    {
        WCHAR ch = s[s.GetLength() - 1];
        if ((ch >= 0x20 && ch <= 0xD7FF) ||
            (ch >= 0xE000 && ch <= 0xFFFD))
        {
            break;
        }
        // 剔除不合法Unicode字符
        s.Truncate(s.GetLength() - 1);
    }

    return msclr::interop::marshal_as<System::String^>(s.GetString());
}


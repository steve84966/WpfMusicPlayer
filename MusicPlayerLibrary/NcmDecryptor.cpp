#include "pch.h"
#include "MusicPlayerLibrary.h"

#include <openssl/evp.h>
#include <cpp-base64/base64.h>
#include <rapidjson/document.h>
#include <rapidjson/error/en.h>

static const uint8_t CORE_KEY[16] = {
    0x68, 0x7a, 0x48, 0x52, 0x41, 0x6d, 0x73, 0x6f,
    0x35, 0x6b, 0x49, 0x6e, 0x62, 0x61, 0x78, 0x57
};
static const uint8_t META_KEY[16] = {
    0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21,
    0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28
};
static const uint8_t MAGIC_HEADER[8] = {
    0x43, 0x54, 0x45, 0x4e, 0x46, 0x44, 0x41, 0x4d
};

static bool BytesHasPrefix(const uint8_t* buf, const uint8_t* prefix, size_t len)
{
    for (size_t i = 0; i < len; ++i)
    {
        if (buf[i] != prefix[i])
            return false;
    }
    return true;
}

static uint32_t ReadLEUint32(const uint8_t* p)
{
    return static_cast<uint32_t>(p[0])
        | static_cast<uint32_t>(p[1]) << 8
        | static_cast<uint32_t>(p[2]) << 16
        | static_cast<uint32_t>(p[3]) << 24;
}

static std::vector<uint8_t> Aes128EcbDecrypt(const std::vector<uint8_t>& cipher, const uint8_t key[16])
{
    std::vector<uint8_t> plain(cipher.size() + 16);
    EVP_CIPHER_CTX* ctx = EVP_CIPHER_CTX_new();
    if (!ctx)
        throw std::runtime_error("EVP_CIPHER_CTX_new failed");
    int outlen1 = 0, outlen2 = 0;
    if (EVP_DecryptInit_ex(ctx, EVP_aes_128_ecb(), nullptr, key, nullptr) != 1)
    {
        EVP_CIPHER_CTX_free(ctx);
        throw std::runtime_error("EVP_DecryptInit_ex failed");
    }
    EVP_CIPHER_CTX_set_padding(ctx, 1);
    if (EVP_DecryptUpdate(ctx, plain.data(), &outlen1, cipher.data(), (int)cipher.size()) != 1)
    {
        EVP_CIPHER_CTX_free(ctx);
        throw std::runtime_error("EVP_DecryptUpdate failed");
    }
    if (EVP_DecryptFinal_ex(ctx, plain.data() + outlen1, &outlen2) != 1)
    {
        EVP_CIPHER_CTX_free(ctx);
        throw std::runtime_error("EVP_DecryptFinal_ex failed");
    }
    plain.resize(outlen1 + outlen2);
    EVP_CIPHER_CTX_free(ctx);
    return plain;
}

static std::vector<uint8_t> Base64DecodeVector(const std::string& s)
{
    std::string decoded = base64_decode(s);
    return { decoded.begin(), decoded.end() };
}

MusicPlayerLibrary::NcmDecryptor::NcmDecryptor(const std::vector<uint8_t>& data, const CString& filename)
    : m_raw(data), m_filename(filename) {
    if (m_raw.size() < 8)
        throw std::runtime_error("ncm file too small");
    if (!BytesHasPrefix(m_raw.data(), MAGIC_HEADER, 8))
        throw std::runtime_error("此ncm文件已损坏");
    m_offset = 10;
}

MusicPlayerLibrary::DecryptResult MusicPlayerLibrary::NcmDecryptor::Decrypt()
{
    auto keyBox = GetKeyBox();
    m_oriMeta = GetMetaData();
    m_audio = GetAudio(keyBox);
    if (!m_oriMeta.format.IsEmpty())
        m_format = m_oriMeta.format;
    else
        m_format = _T("mp3");
    if (m_format == _T("mp3"))
        m_mime = _T("audio/mpeg");
    else if (m_format == _T("flac"))
        m_mime = _T("audio/flac");
    else
        m_mime = _T("application/octet-stream");
    DecryptResult res;
    res.ext = Utf8ToWstring(m_format);
    res.mime = m_mime;
    res.title = Utf8ToWstring(m_oriMeta.musicName);
    res.album = Utf8ToWstring(m_oriMeta.album);
    res.pictureUrl = Utf8ToWstring(m_oriMeta.albumPic);
    CString artistJoined;
    for (auto& arr : m_oriMeta.artist)
    {
        if (!arr.empty())
        {
            if (!artistJoined.IsEmpty()) artistJoined += _T("; ");
            artistJoined += arr[0];
        }
    }
    res.artist = Utf8ToWstring(artistJoined);
    res.audioData = m_audio;
    return res;
}

std::vector<uint8_t> MusicPlayerLibrary::NcmDecryptor::GetKeyData()
{
    if (m_offset + 4 > m_raw.size())
        throw std::runtime_error("invalid ncm key length");
    uint32_t keyLen = ReadLEUint32(&m_raw[m_offset]);
    m_offset += 4;
    if (m_offset + keyLen > m_raw.size())
        throw std::runtime_error("invalid ncm key area");
    std::vector<uint8_t> cipherText(keyLen);
    for (uint32_t i = 0; i < keyLen; ++i)
        cipherText[i] = m_raw[m_offset + i] ^ 0x64;
    m_offset += keyLen;
    auto plain = Aes128EcbDecrypt(cipherText, CORE_KEY);
    if (plain.size() <= 17)
        throw std::runtime_error("key data too short");
    std::vector result(plain.begin() + 17, plain.end());
    return result;
}

std::vector<uint8_t> MusicPlayerLibrary::NcmDecryptor::GetKeyBox()
{
    std::vector<uint8_t> keyData = GetKeyData();
    size_t keyLen = keyData.size();
    if (keyLen == 0)
        throw std::runtime_error("empty keyData");
    std::vector<uint8_t> box(256);
    for (int i = 0; i < 256; ++i)
        box[i] = static_cast<uint8_t>(i);
    uint8_t j = 0;
    for (int i = 0; i < 256; ++i)
    {
        j = static_cast<uint8_t>((box[i] + j + keyData[i % keyLen]) & 0xff);
        std::swap(box[i], box[j]);
    }
    std::vector<uint8_t> keyBox(256);
    for (int i = 0; i < 256; ++i)
    {
        int idx = (i + 1) & 0xff;
        uint8_t si = box[idx];
        uint8_t sj = box[(idx + si) & 0xff];
        keyBox[i] = box[(si + sj) & 0xff];
    }
    return keyBox;
}

MusicPlayerLibrary::NcmMusicMeta MusicPlayerLibrary::NcmDecryptor::GetMetaData()
{
    if (m_offset + 4 > m_raw.size())
        throw std::runtime_error("invalid meta length");
    uint32_t metaDataLen = ReadLEUint32(&m_raw[m_offset]);
    m_offset += 4;
    if (metaDataLen == 0)
        return NcmMusicMeta{};
    if (m_offset + metaDataLen > m_raw.size())
        throw std::runtime_error("invalid meta area");
    std::vector<uint8_t> metaData(metaDataLen);
    for (uint32_t i = 0; i < metaDataLen; ++i)
        metaData[i] = m_raw[m_offset + i] ^ 0x63;
    m_offset += metaDataLen;
    if (metaData.size() <= 22)
        throw std::runtime_error("meta data too short");
    std::string base64Str(metaData.begin() + 22, metaData.end());
    auto decoded = Base64DecodeVector(base64Str);
    auto plain = Aes128EcbDecrypt(decoded, META_KEY);
    std::string text(plain.begin(), plain.end());
    size_t labelIndex = text.find(':');
    if (labelIndex == std::string::npos)
        throw std::runtime_error("invalid meta text");
    std::string label = text.substr(0, labelIndex);
    std::string jsonStr = text.substr(labelIndex + 1);
    using namespace rapidjson;
    Document doc;
    doc.Parse(jsonStr.c_str());
    if (doc.HasParseError())
    {
        throw std::runtime_error(std::string("meta json parse error: ") + GetParseError_En(doc.GetParseError()));
    }
    NcmMusicMeta result;
    auto parseMusicMeta = [&](const Value& v, NcmMusicMeta& out)
        {
            if (v.HasMember("musicName") && v["musicName"].IsString())
                out.musicName = Utf8ToWstring(CString(v["musicName"].GetString()));
            if (v.HasMember("album") && v["album"].IsString())
                out.album = Utf8ToWstring(CString(v["album"].GetString()));
            if (v.HasMember("format") && v["format"].IsString())
                out.format = Utf8ToWstring(CString(v["format"].GetString()));
            if (v.HasMember("albumPic") && v["albumPic"].IsString())
                out.albumPic = Utf8ToWstring(CString(v["albumPic"].GetString()));
            if (v.HasMember("artist") && v["artist"].IsArray())
            {
                const auto& arr = v["artist"];
                for (SizeType i = 0; i < arr.Size(); ++i)
                {
                    std::vector<CString> oneArtist;
                    const auto& item = arr[i];
                    if (item.IsArray())
                    {
                        for (SizeType j = 0; j < item.Size(); ++j)
                        {
                            if (item[j].IsString())
                                oneArtist.emplace_back(Utf8ToWstring(CString(item[j].GetString())));
                            else if (item[j].IsNumber())
                            {
                                oneArtist.emplace_back(std::to_string(item[j].GetInt64()).c_str());
                            }
                        }
                    }
                    else if (item.IsString())
                    {
                        oneArtist.emplace_back(Utf8ToWstring(CString(item.GetString())));
                    }
                    else if (item.IsNumber())
                    {
                        oneArtist.emplace_back(std::to_string(item.GetInt64()).c_str());
                    }
                    if (!oneArtist.empty())
                        out.artist.push_back(std::move(oneArtist));
                }
            }
        };
    if (label == "dj")
    {
        if (!doc.HasMember("mainMusic") || !doc["mainMusic"].IsObject())
            throw std::runtime_error(
                "dj meta missing mainMusic");
        parseMusicMeta(doc["mainMusic"], result);
    }
    else { parseMusicMeta(doc, result); }
    if (!result.albumPic.IsEmpty())
    {
        // if (result.albumPic.rfind("http://", 0) == 0) result.albumPic.replace(0, 7, "https://");
        if (result.albumPic.Find(_T("http://")) == 0)
            result.albumPic = _T("https://") + result.albumPic.Mid(7);
        result.albumPic += "?param=500y500";
    }
    ATLTRACE(_T("info: NcmDecrypt success!\n"));
    ATLTRACE(_T("info: music name: %s\n"), result.musicName.GetString());
    ATLTRACE(_T("info: album: %s\n"), result.album.GetString());
    ATLTRACE(_T("info: artist: %s\n"), result.artist[0][0].GetString());
    ATLTRACE(_T("info: album pic: %s\n"), result.albumPic.GetString());
    ATLTRACE(_T("info: format: %s\n"), result.format.GetString());
    ATLTRACE(_T("info: meta data: %s\n"), Utf8ToWstring(CString(jsonStr.c_str())).GetString());
    return result;
}

std::vector<uint8_t> MusicPlayerLibrary::NcmDecryptor::GetAudio(const std::vector<uint8_t>& keyBox)
{
    if (m_offset + 9 > m_raw.size())
        throw std::runtime_error("invalid audio offset");
    uint32_t dataLen = ReadLEUint32(&m_raw[m_offset + 5]);
    size_t newOffset = m_offset + static_cast<size_t>(dataLen) + 13;
    if (newOffset > m_raw.size())
        throw std::runtime_error("audio offset out of range");
    m_offset = newOffset;
    std::vector audio(m_raw.begin() + m_offset, m_raw.end());
    for (size_t i = 0; i < audio.size(); ++i)
        audio[i] ^= keyBox[i & 0xff];
    return audio;
}

CString MusicPlayerLibrary::NcmDecryptor::Utf8ToWstring(const CString& s)
{
    if (s.IsEmpty())
        return {};

    CStringA utf8Str(s);

    int wideLen = MultiByteToWideChar(CP_UTF8, 0, utf8Str.GetString(), -1, nullptr, 0);
    if (wideLen <= 0)
        return {};

    CString result;
    wchar_t* buffer = result.GetBufferSetLength(wideLen);
    MultiByteToWideChar(CP_UTF8, 0, utf8Str.GetString(), -1, buffer, wideLen);
    result.ReleaseBuffer();

    return result;
}

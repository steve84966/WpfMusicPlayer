#include "pch.h"
#include "AtlTraceRedirect.h"
#include <ctime>
#include <iomanip>
#include <sstream>

AtlTraceRedirect* AtlTraceRedirect::global_atl_trace_redirector;

AtlTraceRedirect::AtlTraceRedirect(const wchar_t* path, bool append)
    : file_p(nullptr)
    , own_file(true)
    , enable_redirect(false)
    , timestamp_enable(true)
    , info_enable(true)
{
    if (path != nullptr && wcslen(path) > 0)
    {
        const wchar_t* mode = append ? L"a" : L"w";
        if (errno_t err = _wfopen_s(&file_p, path, mode);
            err == 0 && file_p != nullptr)
        {
            setvbuf(file_p, nullptr, _IONBF, 0);
            enable_redirect = true;

            // 写入开始标记
            CSingleLock file_mut_lock(&file_mut);
            CStringA start_msg;
            start_msg.Format("=== ATLTRACE Redirect Started at %s ===\n",
                query_time_stamp().GetString());
            fputs(start_msg, file_p);
        }
    }
}

AtlTraceRedirect::AtlTraceRedirect(FILE* file_ptr, bool take_ownership)
    : file_p(file_ptr)
    , own_file(take_ownership)
    , enable_redirect(file_ptr != nullptr)
    , timestamp_enable(true)
    , info_enable(true)
{
    if (file_p != nullptr)
    {
        CSingleLock file_mut_lock(&file_mut);
        CStringA start_msg;
        start_msg.Format("=== ATLTRACE Redirect Started at %s ===\n",
            query_time_stamp().GetString());
        fputs(start_msg, file_p);
    }
}

AtlTraceRedirect::~AtlTraceRedirect()
{
    if (file_p != nullptr)
    {
        {
            CSingleLock file_mut_lock(&file_mut);
            CStringA end_msg;
            end_msg.Format("=== ATLTRACE Redirect Ended at %s ===\n",
                query_time_stamp().GetString());
            fputs(end_msg, file_p);
        }

        if (own_file)
        {
            fclose(file_p);
        }
        file_p = nullptr;
    }
}

void AtlTraceRedirect::Enable()
{
    if (file_p != nullptr)
    {
        enable_redirect = true;
    }
}

void AtlTraceRedirect::Disable()
{
    enable_redirect = false;
}

void AtlTraceRedirect::flush_stream()
{
    if (file_p != nullptr)
    {
        CSingleLock file_mut_lock(&file_mut);
        fflush(file_p);
    }
}

CStringA AtlTraceRedirect::query_time_stamp() const
{
    auto now = std::chrono::system_clock::now();
    auto now_time_t = std::chrono::system_clock::to_time_t(now);
    auto now_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()) % 1000;

    struct tm timeinfo;
    localtime_s(&timeinfo, &now_time_t);

    char buffer[64];
    sprintf_s(buffer, sizeof(buffer), "%04d-%02d-%02d %02d:%02d:%02d.%03lld",
        timeinfo.tm_year + 1900,
        timeinfo.tm_mon + 1,
        timeinfo.tm_mday,
        timeinfo.tm_hour,
        timeinfo.tm_min,
        timeinfo.tm_sec,
        now_ms.count());

    return { buffer };
}

void AtlTraceRedirect::write_log(const char* file_name_full, int line_num, const char* message)
{
    if (!enable_redirect || file_p == nullptr || message == nullptr)
        return;

    CSingleLock file_mut_lock(&file_mut);

    CStringA log_line;

    if (timestamp_enable)
    {
        log_line += "[";
        log_line += query_time_stamp();
        log_line += "] ";
    }

    if (info_enable && file_name_full != nullptr && line_num > 0)
    {
        const char* file_name = strrchr(file_name_full, '\\');
        if (file_name == nullptr)
            file_name = strrchr(file_name_full, '/');

        if (file_name != nullptr)
            file_name++;
        else
            file_name = file_name_full;

        CStringA fileInfo;
        fileInfo.Format("[%s:%d] ", file_name, line_num);
        log_line += fileInfo;
    }

    log_line += message;

    if (log_line.IsEmpty() || log_line[log_line.GetLength() - 1] != '\n')
    {
        log_line += "\n";
    }

    fputs(log_line, file_p);
}

CStringA AtlTraceRedirect::format_message_va(const wchar_t* format, va_list args)
{
    if (format == nullptr)
        return {};

    CStringW wide_msg;
    wide_msg.FormatV(format, args);

    return CStringA(wide_msg);
}

CStringA AtlTraceRedirect::format_message_va(const char* format, va_list args)
{
    if (format == nullptr)
        return {};

    CStringA msg;
    msg.FormatV(format, args);
    return msg;
}

void AtlTraceRedirect::TraceEx(const char* file_name, int line_num, const wchar_t* format, ...)
{
    if (!enable_redirect || file_p == nullptr || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(file_name, line_num, message);
}

void AtlTraceRedirect::TraceEx(const char* file_name, int line_num, const char* format, ...)
{
    if (!enable_redirect || file_p == nullptr || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(file_name, line_num, message);
}

void AtlTraceRedirect::Trace(const wchar_t* format, ...)
{
    if (!enable_redirect || file_p == nullptr || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(nullptr, -1, message);
}

void AtlTraceRedirect::Trace(const char* format, ...)
{
    if (!enable_redirect || file_p == nullptr || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(nullptr, -1, message);
}

AtlTraceRedirect* AtlTraceRedirect::GetAtlTraceRedirector()
{
    return global_atl_trace_redirector;
}

void AtlTraceRedirect::SetAtlTraceRedirector(AtlTraceRedirect* redirector)
{
    global_atl_trace_redirector = redirector;
}

#include "pch.h"
#include "AtlTraceRedirect.h"
#include <ctime>
#include <iomanip>
#include <sstream>

AtlTraceRedirect* AtlTraceRedirect::global_atl_trace_redirector;


AtlTraceRedirect::AtlTraceRedirect(System::Object^ loggerObj)
    : logger(loggerObj)
    , enable_redirect(true)
    , timestamp_enable(true)
    , info_enable(true)
{
}

AtlTraceRedirect::~AtlTraceRedirect()
{
}

void AtlTraceRedirect::Enable()
{
    if (!System::Object::ReferenceEquals(logger, nullptr))
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
    if (!enable_redirect || System::Object::ReferenceEquals(logger, nullptr) || message == nullptr)
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

    if (!log_line.IsEmpty() && log_line[log_line.GetLength() - 1] == '\n')
    {
        log_line.Remove('\n');
    }

    System::String^ managedLog = gcnew System::String(log_line);

    System::Type^ loggerType = logger->GetType();
    array<System::Type^>^ paramTypes = gcnew array<System::Type^>(1) { System::String::typeid };
    System::Reflection::MethodInfo^ logMethod = loggerType->GetMethod("LogInformation", paramTypes);

    if (logMethod != nullptr)
    {
        array<System::Object^>^ args = gcnew array<System::Object^>(1) { managedLog };
        logMethod->Invoke(logger, args);
    }
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
    if (!enable_redirect || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(file_name, line_num, message);
}

void AtlTraceRedirect::TraceEx(const char* file_name, int line_num, const char* format, ...)
{
    if (!enable_redirect || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(file_name, line_num, message);
}

void AtlTraceRedirect::Trace(const wchar_t* format, ...)
{
    if (!enable_redirect || format == nullptr)
        return;

    va_list args;
    va_start(args, format);
    CStringA message = format_message_va(format, args);
    va_end(args);

    write_log(nullptr, -1, message);
}

void AtlTraceRedirect::Trace(const char* format, ...)
{
    if (!enable_redirect || format == nullptr)
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

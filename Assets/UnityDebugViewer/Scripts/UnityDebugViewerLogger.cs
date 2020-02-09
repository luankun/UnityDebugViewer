﻿using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using UnityEngine;

namespace UnityDebugViewer
{
    /// <summary>
    /// socket用于传递log数据的structure
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)] //按1字节对齐
    public struct TransferLogData
    {
        public int logType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string info;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string stack;

        public TransferLogData(string _info, string _stack, LogType type)
        {
            var infoLength = _info.Length > 512 ? 512 : _info.Length;
            info = _info.Substring(0, infoLength);
            var stackLength = _stack.Length > 1024 ? 1024 : _stack.Length;
            stack = _stack.Substring(0, stackLength);
            logType = (int)type;
        }
    }

    [Serializable]
    public struct CollapsedLogData
    {
        public LogData log;
        public int count;
    }

    /// <summary>
    /// 保存log数据
    /// </summary>
    [Serializable]
    public class LogData  
    {
        /// <summary>
        /// Regular expression for all the stack message generated by unity
        /// </summary>
        public const string UNITY_STACK_REGEX = @"(?<className>[\w]+(\.[\w]+)*)[\.:](?<methodName>[\w]+[\s]*\(.*\))[\s]*\([at]*\s*(?<filePath>(.+:[\\/])?(.+[\\/])*[\w]+\.[\w]+)\:(?<lineNumber>[\d]+)\)";

        /// <summary>
        /// Regular expression for the stack message generated by unity when compiling
        /// </summary>
        public const string UNITY_COMPILE_LOG_REGEX = @"(?<filePath>(.+:[\\/])?(.+[\\/])*[\w]+\.[\w]+)\((?<lineNumber>[\d]+).+\):";
        
        public const string LOG_FILE_STACK_REGEX = @"(?<className>([\S]+(\.[\S]+)*)):(?<methodName>[\S]+\(.*\))";

        public string info { get; private set; }
        public string extraInfo { get; private set; }
        public LogType type { get; private set; }
        public string stackMessage { get; private set; }

        private List<LogStackData> _stackList;
        public List<LogStackData> stackList
        {
            get
            {
                if(_stackList == null)
                {
                    _stackList = new List<LogStackData>();
                }

                return _stackList;
            }
        }


        public LogData(string info, string stack, LogType type)
        {
            this.info = info;
            this.type = type;
            this.stackMessage = stack;

            /// stack message is null means that it is generated by compilation
            if (string.IsNullOrEmpty(stack))
            {
                bool isMatch = Regex.IsMatch(info, UNITY_COMPILE_LOG_REGEX);
                if (isMatch)
                {
                    Match match = Regex.Match(info, UNITY_COMPILE_LOG_REGEX);
                    var logStack = new LogStackData(match);
                    this.stackList.Add(logStack);
                    this.info = Regex.Replace(info, UNITY_COMPILE_LOG_REGEX, "").Trim();
                    this.stackMessage = logStack.fullStackMessage;
                }
                return;
            }

            MatchCollection matchList = null;
            string regex = string.Empty;
            if (Regex.IsMatch(stack, UNITY_STACK_REGEX))
            {
                regex = UNITY_STACK_REGEX;
            }
            else if(Regex.IsMatch(stack, LOG_FILE_STACK_REGEX))
            {
                regex = LOG_FILE_STACK_REGEX;
            }

            matchList = Regex.Matches(stack, regex);
            if (matchList != null)
            {
                this.stackList.Clear();
                foreach (Match match in matchList)
                {
                    if (match == null)
                    {
                        continue;
                    }
                    this.stackList.Add(new LogStackData(match));
                }
            }

            /// get the extraInfo of log
            /// usually it is the calling function that generates log
            this.extraInfo = Regex.Replace(stack, regex, "").Trim();
        }


        public LogData(string info, string extraInfo, List<StackFrame> stackFrameList, LogType logType)
        {
            this.info = info;
            this.type = logType;
            this.extraInfo = extraInfo;
            this.stackMessage = extraInfo;

            if (stackFrameList == null)
            {
                return;
            }

            for(int i = 0; i < stackFrameList.Count; i++)
            {
                var logStackData = new LogStackData(stackFrameList[i]);
                this.stackMessage = string.Format("{0}\n{1}", this.stackMessage, logStackData.fullStackMessage);
                this.stackList.Add(logStackData);
            }
        }

        public LogData(string info, string extraInfo, string stack, List<LogStackData> stackList, LogType logType)
        {
            this.info = info;
            this.extraInfo = extraInfo;
            this.stackMessage = stack;
            this.stackList.AddRange(stackList);
            this.type = logType;
        }

        public string GetKey()
        {
            string key = string.Format("{0}{1}{2}", info, stackMessage, type);
            return key;
        }

        public bool Equals(LogData data)
        {
            if (data == null)
            {
                return false;
            }

            return this.info.Equals(data.info) && this.stackMessage.Equals(data.stackMessage) && this.type == data.type;
        }

        public LogData Clone()
        {
            LogData log = new LogData(this.info, this.extraInfo, this.stackMessage, this.stackList, this.type);
            return log;
        }
    }


    [Serializable]
    public class LogStackData
    {
        public string className { get; private set; }
        public string methodName { get; private set; }
        public string filePath { get; private set; }
        public int lineNumber { get; private set; }

        public string fullStackMessage { get; private set; }
        public string sourceContent;

        public LogStackData(Match match)
        {
            this.className = match.Result("${className}");
            this.methodName = match.Result("${methodName}");
            
            this.filePath = UnityDebugViewerEditorUtility.ConvertToSystemFilePath(match.Result("${filePath}"));
            string lineNumberStr = match.Result("${lineNumber}");
            int lineNumber;
            this.lineNumber = int.TryParse(lineNumberStr, out lineNumber) ? lineNumber : -1;

            if (this.className.Equals("${className}") || this.methodName.Equals("${methodName}"))
            {
                if (this.className.Equals("${className}"))
                {
                    this.className = "UnknowClass";
                }

                if (this.methodName.Equals("${methodName}"))
                {
                    this.methodName = "UnknowMethod";
                }
                this.fullStackMessage = string.Format("at {0}:{1}", this.filePath, this.lineNumber);
            }
            else if (this.filePath.Equals("${filePath}") || this.lineNumber == -1)
            {
                this.fullStackMessage = string.Format("{0}:{1}", this.className, this.methodName);
            }
            else
            {
                this.fullStackMessage = string.Format("{0}:{1} (at {2}:{3})", this.className, this.methodName, this.filePath, this.lineNumber);
            }

            this.sourceContent = String.Empty;
        }

        public LogStackData(StackFrame stackFrame)
        {
            var method = stackFrame.GetMethod();

            string methodParam = string.Empty;
            var paramArray = method.GetParameters();
            if (paramArray != null)
            {
                string[] paramType = new string[paramArray.Length];
                for (int index = 0; index < paramArray.Length; index++)
                {
                    paramType[index] = paramArray[index].ParameterType.Name;
                }
                methodParam = string.Join(", ", paramType);
            }

            this.className = method.DeclaringType.Name;
            this.methodName = string.Format("{0}({1})", method.Name, methodParam); ;
            this.filePath = stackFrame.GetFileName();
            this.lineNumber = stackFrame.GetFileLineNumber();

            this.fullStackMessage = string.Format("{0}:{1} (at {2}:{3})", this.className, this.methodName, this.filePath, this.lineNumber);
            this.sourceContent = String.Empty;
        }

        public bool Equals(LogStackData data)
        {
            if(data == null)
            {
                return false;
            }

            return fullStackMessage.Equals(data.fullStackMessage);
        }
    }

    /// <summary>
    /// Attribute to mark whether the stack message of target method should be ignore when parsing the system stack trace message
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class IgnoreStackTrace : Attribute
    {
        /// <summary>
        /// Decide if the target method is displayed as extraInfo
        /// </summary>
        public bool showAsExtraInfo { get; private set; }

        public IgnoreStackTrace(bool show)
        {
            showAsExtraInfo = show;
        }

        public IgnoreStackTrace()
        {
            /// don't diaplay as extraInfo in default
            showAsExtraInfo = false;
        }
    }


    public class UnityDebugViewerLogger
    {
        /// <summary>
        /// Regular expression for the stack message gathered from logcat process
        /// </summary>
        private const string LOGCAT_REGEX = @"(?<time>[\d]+-[\d]+[\s]*[\d]+:[\d]+:[\d]+.[\d]+)[\s]*(?<logType>\w)/(?<filter>[\w]*)[\s]*\([\s\d]*\)[\s:]*";

        private const string LOG_FILE_REGEX_PART_ONE = @"\[(?<logType>[\w]+)\][\s]*(?<time>[\d]+:[\d]+:[\d]+\.[\d]+\|[\d]+)";
        private const string LOG_FILE_REGEX_PART_TWO = @"(?<stack>([\S]+(\.[\S]+)*:[\S]+\(.*\)[\s]*)+)";

        /// <summary>
        /// Add log to the UnityDebugViewerEditor correspond to 'Editor'
        /// </summary>
        /// <param name="transferLogData"></param>
        public static void AddEditorLog(string info, string stack, LogType type)
        {
            UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.Editor;
            AddLog(info, stack, type, editorType);
        }

        /// <summary>
        /// Add log to the UnityDebugViewerEditor correspond to 'ADBForward'
        /// </summary>
        /// <param name="transferLogData"></param>
        public static void AddTransferLog(TransferLogData transferLogData)
        {
            UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.ADBForward;
            LogType type = (LogType)transferLogData.logType;
            string info = transferLogData.info;
            string stack = transferLogData.stack;
            AddLog(info, stack, type, editorType);
        }

        /// <summary>
        /// Add log to the UnityDebugViewerEditor correspond to 'ADBLogcat'
        /// </summary>
        /// <param name="logcat"></param>
        public static void AddLogcatLog(string logcat)
        {
            if (Regex.IsMatch(logcat, LOGCAT_REGEX))
            {
                UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.ADBLogcat;
                var match = Regex.Match(logcat, LOGCAT_REGEX);
                string logType = match.Result("${logType}").ToUpper();
                string tag = match.Result("${tag}");
                string time = match.Result("${time}");
                string info = Regex.Replace(logcat, LOGCAT_REGEX, "");

                LogType type;
                switch (logType)
                {
                    case "I":
                        type = LogType.Log;
                        break;
                    case "W":
                        type = LogType.Warning;
                        break;
                    case "E":
                        type = LogType.Error;
                        break;
                    default:
                        type = LogType.Error;
                        break;
                }
                AddLog(info, string.Empty, type, editorType);
            }
        }

        /// <summary>
        /// Add log to the UnityDebugViewerEditor correspond to 'ADBLogcat'
        /// </summary>
        /// <param name="logcat"></param>
        public static void AddLogFileLog(string logStr)
        {
            UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.LogFile;
            if(Regex.IsMatch(logStr, LOG_FILE_REGEX_PART_ONE))
            {
                var match = Regex.Match(logStr, LOG_FILE_REGEX_PART_ONE);
                string logType = match.Result("${logType}").ToLower();
                string time = match.Result("${time}");

                string info = Regex.Replace(logStr, LOG_FILE_REGEX_PART_ONE, "");
                string stack = String.Empty;
                if(Regex.IsMatch(info, LOG_FILE_REGEX_PART_TWO))
                {
                    match = Regex.Match(info, LOG_FILE_REGEX_PART_TWO);
                    stack = match.Result("${stack}");
                    info = Regex.Replace(info, LOG_FILE_REGEX_PART_TWO, "");
                }

                LogType type;
                switch (logType)
                {
                    case "log":
                        type = LogType.Log;
                        break;
                    case "warning":
                        type = LogType.Warning;
                        break;
                    case "error":
                        type = LogType.Error;
                        break;
                    default:
                        type = LogType.Error;
                        break;

                }

                AddLog(info.Trim(), stack, type, editorType);
            }
        }

        public static void AddLog(string info, string stack, LogType type, UnityDebugViewerEditorType editorType)
        {
            var logData = new LogData(info, stack, type);
            AddLog(logData, editorType);
        }

        public static void AddLog(string info, string extraMessage, List<StackFrame> stackFrameList, LogType type, UnityDebugViewerEditorType editorType)
        {
            var logData = new LogData(info, extraMessage, stackFrameList, type);
            AddLog(logData, editorType);
        }

        /// <summary>
        /// Add log to target UnityDebugViewerEditor
        /// </summary>
        /// <param name="data"></param>
        /// <param name="editorType"></param>
        public static void AddLog(LogData data, UnityDebugViewerEditorType editorType)
        {
            UnityDebugViewerEditorManager.GetEditor(editorType).AddLog(data);
        }

        [IgnoreStackTrace(true)]
        public static void Log(string str, UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.Editor)
        {
            AddSystemLog(str, LogType.Log, editorType);
        }

        [IgnoreStackTrace(true)]
        public static void LogWarning(string str, UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.Editor)
        {
            AddSystemLog(str, LogType.Warning, editorType);
        }

        [IgnoreStackTrace(true)]
        public static void LogError(string str, UnityDebugViewerEditorType editorType = UnityDebugViewerEditorType.Editor)
        {
            AddSystemLog(str, LogType.Error, editorType);
        }

        [IgnoreStackTrace]
        private static void AddSystemLog(string str, LogType logType, UnityDebugViewerEditorType editorType)
        {
            string extraInfo = string.Empty;
            var stackList = ParseSystemStackTrace(ref extraInfo);
            AddLog(str, extraInfo, stackList, logType, editorType);
        }

        [IgnoreStackTrace]
        private static List<StackFrame> ParseSystemStackTrace(ref string extraInfo)
        {
            List<StackFrame> stackFrameList = new List<StackFrame>();

            StackTrace stackTrace = new StackTrace(true);
            StackFrame[] stackFrames = stackTrace.GetFrames();

            for (int i = 0; i < stackFrames.Length; i++)
            {
                StackFrame stackFrame = stackFrames[i];
                var method = stackFrame.GetMethod();

                if (!method.IsDefined(typeof(IgnoreStackTrace), true))
                {
                    /// ignore all the stack message generated by Unity internal method
                    if (method.Name.Equals("InternalInvoke"))
                    {
                        break;
                    }

                    stackFrameList.Add(stackFrame);
                }
                else
                {
                    foreach (object attributes in method.GetCustomAttributes(false))
                    {
                        IgnoreStackTrace ignoreAttr = (IgnoreStackTrace)attributes;
                        /// check and display corresponding method as extraInfo
                        if (ignoreAttr != null && ignoreAttr.showAsExtraInfo)
                        {
                            string methodParam = string.Empty;
                            var paramArray = method.GetParameters();
                            if (paramArray != null)
                            {
                                string[] paramType = new string[paramArray.Length];
                                for (int index = 0; index < paramArray.Length; index++)
                                {
                                    paramType[index] = paramArray[index].ParameterType.Name;
                                }
                                methodParam = string.Join(", ", paramType);
                            }

                            extraInfo = string.Format("{0}:{1}({2})", method.DeclaringType.FullName, method.Name, methodParam);
                        }
                    }
                }
            }

            return stackFrameList;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KizhiPart3
{
    public class Debugger
    { 
        private readonly TextWriter _writer;

        private readonly Dictionary<string, FuncInfo> _functions = new Dictionary<string, FuncInfo>();
        private readonly List<CommandModel> _codeLines = new List<CommandModel>();
        private IDebuggerSession _currentSession;
        private readonly string[] _codeLinesSeparators = new string[] {"\r", "\n", "\r\n"};

        public Debugger(TextWriter writer)
        {
            _writer = writer;
        }

        public void ExecuteLine(string command)
        {
            if (command == "set code" || command == "end set code") return;
            switch (command)
            {
                case "run":
                    SafeGetDebuggingSession().RunToNextBreakPoint();
                    return;
                case "step":
                    SafeGetDebuggingSession().Step();
                    return;
                case "step over":
                    SafeGetDebuggingSession().StepOver();
                    return;
                case "print trace":
                    SafeGetDebuggingSession().PrintTrace();
                    return;
                case "print mem":
                    SafeGetDebuggingSession().PrintMem();
                    return;
            }
            if (command.StartsWith("add break"))
            {
                var addingBreakPoint = int.Parse(command.Substring(9));
                SafeGetDebuggingSession().AddBreakPoint(addingBreakPoint);
                return;
            }
            Interpret(command);
        }

        private IDebuggerSession SafeGetDebuggingSession()
        {
            if (_currentSession == null || _currentSession.IsEnded)
                _currentSession = new DebuggerSession(_writer, new VariablesExecutor(_writer), _codeLines, _functions );
            return _currentSession;
        }

        private void Interpret(string code)
        {
            var parsedCode = code.Split(_codeLinesSeparators, 
                StringSplitOptions.RemoveEmptyEntries);
            var lastFuncName = "";

            for (var i = 0; i < parsedCode.Length; i++)
            {
                var codeLine = parsedCode[i];
                CommandModel commandModel;
                if (codeLine.StartsWith("    "))
                {
                    commandModel = ParseCodeLine(codeLine.TrimStart());
                    _functions[lastFuncName].LastLineIndex++;
                }

                else if (codeLine.StartsWith("def"))
                {
                    commandModel = ParseCodeLine(codeLine);
                    var funcName = commandModel.CommandParameters[0];
                    _functions[funcName] = new FuncInfo(i);
                    lastFuncName = funcName;
                }
                else
                {
                    commandModel = ParseCodeLine(codeLine);
                }
                _codeLines.Add(commandModel);
            }
        }

        private CommandModel ParseCodeLine(string codeLine)
        {
            var parsedCodeLine = codeLine.Split(' ');
            return new CommandModel(parsedCodeLine[0], parsedCodeLine.Skip(1).ToArray());
        }
    }

    internal interface IDebuggerSession
    {
        bool IsEnded { get; }
        void RunToNextBreakPoint();
        void Step();
        void StepOver();
        void PrintMem();
        void PrintTrace();
        void AddBreakPoint(int lineIndex);
    }
    
    internal class DebuggerSession : IDebuggerSession
    {
        private readonly TextWriter _writer;
        private readonly IReadOnlyList<CommandModel> _codeLines;
        private readonly IReadOnlyDictionary<string, FuncInfo> _functions;
        private readonly IVariablesExecutor _variablesExecutor;
        private readonly Dictionary<string, Action<int, string[]>> _variablesCommandsDelegator;

        private readonly Stack<StackTraceInfo> _stackTrace = new Stack<StackTraceInfo>();
        
        private readonly HashSet<int> _currentRunningBreakPoints = new HashSet<int>();

        private int _currentPosition;

        public bool IsEnded => _currentPosition >= _codeLines.Count;
        
        public DebuggerSession(TextWriter writer, IVariablesExecutor variablesExecutor, IReadOnlyList<CommandModel> codeLines,
            IReadOnlyDictionary<string, FuncInfo> functions)
        {
            _writer = writer;
            _variablesExecutor = variablesExecutor;
            _functions = functions;
            _codeLines = codeLines;

            _variablesCommandsDelegator = new Dictionary<string, Action<int, string[]>>
            {
                ["set"] = (callLine, parameters) => 
                    _variablesExecutor.Set(callLine, parameters[0], int.Parse(parameters[1])),
                ["sub"] = (callLine, parameters) => 
                    _variablesExecutor.Sub(callLine, parameters[0], int.Parse(parameters[1])),
                ["print"] = (callLine, parameters) => 
                    _variablesExecutor.Print(parameters[0]),
                ["rem"] = (callLine, parameters) => 
                    _variablesExecutor.Remove(parameters[0]),
            };
        }

        public void RunToNextBreakPoint()
        {
            while (!IsEnded)
            {
                Step();
                if (_currentRunningBreakPoints.Contains(_currentPosition))
                    break;
            }
        }

        public void Step()
        {
            var currentCommand = _codeLines[_currentPosition];
            if (currentCommand.CommandName == "def")
            {
                var funcName = currentCommand.CommandParameters[0];
                _currentPosition = _functions[funcName].LastLineIndex + 1;
                return;
            }

            if (currentCommand.CommandName == "call")
            {
                var funcName = currentCommand.CommandParameters[0];
                var funcInfo = GetFuncInfo(funcName);
                _stackTrace.Push(new StackTraceInfo(funcName, _currentPosition, funcInfo.LastLineIndex));
                _currentPosition = funcInfo.DefineLineIndex + 1;
                return;
            }

            _variablesCommandsDelegator[currentCommand.CommandName](_currentPosition, currentCommand.CommandParameters);
            _currentPosition = CleanUpStackTraceIfNeedAndGetPointerToReturn();
            _currentPosition++;
        }

        public void StepOver()
        {
            var currentCommand = _codeLines[_currentPosition];
            if (currentCommand.CommandName != "call")
            {
                Step();
                return;
            }

            var funcName = currentCommand.CommandParameters[0];
            var funcInfo = _functions[funcName];

            var lineAfterFuncIndex = GetPointerToReturnIfStepOver() + 1;
            while (_currentPosition != lineAfterFuncIndex)
            {
                Step();
            }
        }

        private int CleanUpStackTraceIfNeedAndGetPointerToReturn()
        {
            if (_stackTrace.Count == 0) return _currentPosition;
            var possiblePosition = _currentPosition;
            while (_stackTrace.Count!=0)
            {
                if (possiblePosition != _stackTrace.Peek().LastLine)
                    break;
                possiblePosition = _stackTrace.Pop().CallLine;
            }

            return possiblePosition;
        }

        private int GetPointerToReturnIfStepOver()
        {
            if (_stackTrace.Count == 0) return _currentPosition;
            var possiblePosition = _currentPosition;
            foreach (var stackTraceInfo in _stackTrace.TakeWhile(stackTraceInfo => possiblePosition == stackTraceInfo.LastLine))
            {
                possiblePosition = stackTraceInfo.CallLine;
            }
            return possiblePosition;
        }

        private FuncInfo GetFuncInfo(string funcName)
        {
            if (!_functions.TryGetValue(funcName, out var funcInfo))
                throw new ArgumentException($"Function {funcName} was not found");
            return funcInfo;
        }

        public void AddBreakPoint(int lineIndex)
        {
            _currentRunningBreakPoints.Add(lineIndex);
        }

        public void PrintMem()
        {
            foreach (var (variableName, variableModel) in _variablesExecutor)
            {
                _writer.WriteLine($"{variableName} {variableModel.Value} {variableModel.LastChangingLineIndex}");
            }
        }

        public void PrintTrace()
        {
            foreach (var stackTraceRecord in _stackTrace)
            {
                _writer.WriteLine($"{stackTraceRecord.CallLine} {stackTraceRecord.FuncName}");
            }
        }
    }

    internal interface IVariablesExecutor : IEnumerable<(string, VariableModel)>
    {
        void Set(int line, string variableName, int settingValue);

        void Sub(int line, string variableName, int subbingValue);

        void Print(string variableName);

        void Remove(string variableName);
    }

    internal class VariablesExecutor : IVariablesExecutor
    {
        private readonly TextWriter _writer;
        private readonly Dictionary<string, VariableModel> _variables = new Dictionary<string, VariableModel>();
        private const string ErrorMessage = "Переменная отсутствует в памяти";

        public VariablesExecutor(TextWriter writer)
        {
            _writer = writer;
        }
        public void Set(int settingLine, string variableName, int settingValue)
        {
            if (_variables.TryGetValue(variableName, out var record))
            {
                record.Value = settingValue;
                record.LastChangingLineIndex = settingLine;
                return;
            }
            _variables[variableName] = new VariableModel(settingValue, settingLine);
        }

        public void Sub(int subbingLine, string variableName, int subbingValue)
        {
            if (!_variables.TryGetValue(variableName, out var record))
            {
                _writer.WriteLine(ErrorMessage);
                return;
            }

            record.LastChangingLineIndex = subbingLine;
            record.Value -= subbingValue;
        }

        public void Print(string variableName)
        {
            _writer.WriteLine(_variables.TryGetValue(variableName, out var value) ? 
                value.Value.ToString() : ErrorMessage);
        }

        public void Remove(string variableName)
        {
            if (!_variables.ContainsKey(variableName))
            {
                _writer.WriteLine(ErrorMessage);
                return;
            }

            _variables.Remove(variableName);
        }

        public IEnumerator<(string, VariableModel)> GetEnumerator()
        {
            return _variables.Select(variableInfo => 
                (variableInfo.Key, variableInfo.Value)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal class CommandModel
    {
        public string CommandName { get; }
        public string[] CommandParameters { get; }

        public CommandModel(string commandName, string[] commandParameters)
        {
            CommandName = commandName;
            CommandParameters = commandParameters;
        }
    }

    internal class VariableModel
    {
        public int Value { get; set; }
        public int LastChangingLineIndex { get; set; }

        public VariableModel(int value, int lastChangingIndex)
        {
            Value = value;
            LastChangingLineIndex = lastChangingIndex;
        }
    }
    
    internal class FuncInfo
    {
        public int DefineLineIndex { get; }
        public int LastLineIndex { get; set; }

        public FuncInfo(int defineLineIndex)
        {
            DefineLineIndex = defineLineIndex;
            LastLineIndex = defineLineIndex;
        }
        
    }

    internal class StackTraceInfo
    {
        public string FuncName { get; }
        public int CallLine { get; }
        public int LastLine { get; set; }

        public StackTraceInfo(string funcName, int callLine, int lastLine)
        {
            FuncName = funcName;
            CallLine = callLine;
            LastLine = lastLine;
        }
    }
}
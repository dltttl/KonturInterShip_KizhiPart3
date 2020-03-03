using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KizhiPart3
{
    public class Debugger
    { 
        private TextWriter _writer;

        private const string ErrorMessage = "Переменная отсутствует в памяти";

        private readonly Dictionary<string, List<CommandModel>> _functions;
        private readonly Dictionary<string, VariableModel> _variables;
        private readonly Dictionary<string, Action<int, string[]>> _variablesCommandDelegator;

        private readonly HashSet<int> _breakPoints;
        private readonly Stack<(int line, string funcName, CommandModel breaker)> _stackTrace;
        private readonly LinkedList<CommandModel> _executionQueue;

        public Debugger(TextWriter writer)
        {
            _writer = writer;
            _functions = new Dictionary<string, List<CommandModel>>();
            _variables = new Dictionary<string, VariableModel>();
            _executionQueue = new LinkedList<CommandModel>();
            _breakPoints = new HashSet<int>();
            _stackTrace = new Stack<(int line, string funcName, CommandModel breaker)>();

            _variablesCommandDelegator = new Dictionary<string, Action<int, string[]>>
            {
                ["set"] = (callingLine, parameters) => Set(callingLine, parameters[0], int.Parse(parameters[1])),
                ["sub"] = (callingLine, parameters) => Sub(callingLine, parameters[0], int.Parse(parameters[1])),
                ["print"] = (callingLine, parameters) => Print(parameters[0]),
                ["rem"] = (callingLine, parameters) => Remove(parameters[0]),
            };
        }

        public void ExecuteLine(string command)
        {
            if (command == "set code") return;
            if (command == "end set code") return;
            if (command == "run")
            {
                Run();
                return;
            }

            if (command.StartsWith("add break"))
            {
                _breakPoints.Add(int.Parse(command.Substring(9)));
                return;
            }

            if (command == "print trace")
            {
                foreach (var (line, funcName, breaker) in _stackTrace)
                {
                    _writer.WriteLine($"{line} {funcName}");
                }

                return;
            }

            if (command == "print mem")
            {
                foreach (var variable in _variables)
                {
                    _writer.WriteLine($"{variable.Key} {variable.Value.Value} {variable.Value.LastChangingLineIndex}");
                }

                return;
            }

            if (command == "step")
            {
                Step();
                return;
            }

            if (command == "step over")
            {
                StepOver();
                return;
            }

            Interpret(command);
        }

        private void Interpret(string code)
        {
            var parsedCode = code.Split(new string[] { "\r\n", "\n", "\r" },
                StringSplitOptions.RemoveEmptyEntries);
            var lastFuncName = "";
            for(var i = 0; i < parsedCode.Length; i++)
            {
                var codeLine = parsedCode[i];
                if (codeLine.StartsWith("    "))
                {
                    _functions[lastFuncName].Add(ParseCodeLine(i, codeLine.Substring(4)));
                    continue;
                }

                if (codeLine.StartsWith("def"))
                {
                    var funcName = codeLine.Substring(4);
                    _functions[funcName] = new List<CommandModel>();
                    lastFuncName = funcName;
                    continue;
                }

                _executionQueue.AddLast(ParseCodeLine(i, codeLine));
            }
        }

        private void InsertFunctionCommand(string funcName)
        {
            var currentFuncCommandsQueue = _functions[funcName];
            for (var i = currentFuncCommandsQueue.Count - 1; i >= 0; i--)
            {
                _executionQueue.AddFirst(currentFuncCommandsQueue[i]);
            }
        }

        private void Run()
        {
            while (_executionQueue.Count > 0)
            {
                var currentCommand = _executionQueue.First.Value;
                if (_breakPoints.Contains(currentCommand.LineIndex))
                {
                    _breakPoints.Remove(currentCommand.LineIndex);
                    return;
                }
                _executionQueue.RemoveFirst();

                if (currentCommand.CommandName == "call")
                {
                    var funcName = currentCommand.CommandParameters[0];
                    _stackTrace.Push((currentCommand.LineIndex, funcName,
                        _executionQueue.Count > 0 ? _executionQueue.First.Value : null));

                    InsertFunctionCommand(funcName);

                    continue;
                }

                _breakPoints.Remove(currentCommand.LineIndex);
                if (_stackTrace.Count > 0 && _stackTrace.Peek().breaker!=null
                    &&currentCommand.LineIndex == _stackTrace.Peek().breaker.LineIndex)
                {
                    _stackTrace.Pop();
                }

                _variablesCommandDelegator[currentCommand.CommandName](currentCommand.LineIndex, currentCommand.CommandParameters);
            }

        }

        private void Step()
        {
            if (_executionQueue.Count == 0) return;
            var currentCommand = _executionQueue.First.Value;
            _executionQueue.RemoveFirst();
            if (currentCommand.CommandName == "call")
            {
                var funcName = currentCommand.CommandParameters[0];
                _stackTrace.Push((currentCommand.LineIndex, funcName,
                    _executionQueue.Count > 0 ? _executionQueue.First.Value : null));

                InsertFunctionCommand(funcName);

                _breakPoints.Remove(_executionQueue.First.Value.LineIndex);
                return;
            }

            _breakPoints.Remove(currentCommand.LineIndex);

            if (_stackTrace.Count > 0 && _stackTrace.Peek().breaker != null
                                      && currentCommand.LineIndex == _stackTrace.Peek().breaker.LineIndex)
            {
                _stackTrace.Pop();
            }

            _variablesCommandDelegator[currentCommand.CommandName](currentCommand.LineIndex, currentCommand.CommandParameters);
        }

        private void StepOver()
        {
            if (_executionQueue.Count == 0) return;

            if (_executionQueue.First.Value.CommandName != "call")
            {
                Step();
                return;
            }

            var currentCommand = _executionQueue.First.Value;
            _executionQueue.RemoveFirst();

            var funcName = currentCommand.CommandParameters[0];
            var breaker = _executionQueue.Count > 0 ? _executionQueue.First.Value : null;
            _stackTrace.Push((currentCommand.LineIndex, funcName, breaker));

            InsertFunctionCommand(funcName);

            _breakPoints.Remove(_executionQueue.Last.Value.LineIndex);

            switch (breaker)
            {
                case null:
                {
                    _breakPoints.Clear();
                    Run();
                    _stackTrace.Clear();
                    break;
                }

                default:
                {
                    while (_executionQueue.First.Value.LineIndex != breaker.LineIndex)
                    {
                        Step();
                    }

                    break;
                }
            }
        }

        private CommandModel ParseCodeLine(int lineIndex, string codeLine)
        {
            var parsedCodeLine = codeLine.Split(' ');
            return new CommandModel(lineIndex, parsedCodeLine[0], parsedCodeLine.Skip(1).ToArray());
        }

        private void Set(int settingLine, string variableName, int settingValue)
        {
            if (_variables.TryGetValue(variableName, out var record))
            {
                record.Value = settingValue;
                record.LastChangingLineIndex = settingLine;
                return;
            }
            _variables[variableName] = new VariableModel(settingValue, settingLine);
        }

        private void Sub(int subbingLine, string variableName, int subbingValue)
        {
            if (!_variables.TryGetValue(variableName, out var record))
            {
                _writer.WriteLine(ErrorMessage);
                return;
            }

            record.LastChangingLineIndex = subbingLine;
            record.Value -= subbingValue;
        }

        private void Print(string variableName)
        {
            _writer.WriteLine(_variables.TryGetValue(variableName, out var value) ? value.Value.ToString() : ErrorMessage);
        }

        private void Remove(string variableName)
        {
            if (!_variables.ContainsKey(variableName))
            {
                _writer.WriteLine(ErrorMessage);
                return;
            }

            _variables.Remove(variableName);
        }
    }

    internal class CommandModel
    {
        public int LineIndex { get; }
        public string CommandName { get; }
        public string[] CommandParameters { get; }

        public CommandModel(int lineIndex, string commandName, string[] commandParameters)
        {
            LineIndex = lineIndex;
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
}
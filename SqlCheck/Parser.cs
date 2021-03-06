﻿using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SqlCheck.Modele;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SqlCheck
{
    public class Parser
    {
        string pathresult = "";
        Chekable chekable;

        Dictionary<string, JTokenType> ondeleteNode = new Dictionary<string, JTokenType>()
        {
             {"ScriptTokenStream",JTokenType.Array }
            ,{"StartOffset",JTokenType.Integer }
            ,{"FragmentLength",JTokenType.Integer }
            ,{"StartLine",JTokenType.Integer }
            ,{"StartColumn",JTokenType.Integer }
            ,{"LastTokenIndex",JTokenType.Integer }
            ,{"FirstTokenIndex",JTokenType.Integer }
        };
        public Parser()
        {
            chekable = new Chekable();
        }
        public Parser(string Connect)
        {
            chekable = new Chekable(Connect);
        }
        //public Parser(string path)
        //{
        //    pathresult = path;
        //}

        public void ParserAll(string dic)
        {
            foreach (string file in Directory.EnumerateFiles(dic, "*.sql", SearchOption.AllDirectories))
            {
                Console.WriteLine("path {0}", file);
                ParserFile(file);
            }
        }

        public List<Message> ParserFile(string path)
        {
            TSql100Parser t = new TSql100Parser(true);

            //using (TextReader sr = new StringReader(""))
            //{
            //}
            using (StreamReader open = File.OpenText(path))
            {
                //var ress = GetUsedTablesFromQuery(open.ReadToEnd());
                IList<ParseError> errors;
                TSqlFragment frag = t.Parse(open, out errors);
                StringBuilder sb = new StringBuilder();
                if (errors != null && errors.Count > 0)
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    foreach (ParseError error in errors)
                    {
                        sb.AppendFormat("{0} {1}\r\n", error.Message, error.Line);
                        chekable.messages.addMessage(Code.T0000006,null, error.Message, error.Line.ToString());
                    }
                    //Console.WriteLine(sb.ToString());
                    //Console.ResetColor();
                }

                var s = frag as TSqlScript;
                if (sb.Length > 0)
                {
                    SaveToFileParseError(path, sb.ToString());
                }
                if (s == null)
                {
                    open.Close();
                    return null;
                }

                foreach (var item in s.Batches)
                {
                    //SqlScriptGeneratorOptions opt = new SqlScriptGeneratorOptions();
                    //Sql100ScriptGenerator gen = new Sql100ScriptGenerator();
                    //string str;
                    //gen.GenerateScript(item, out str);

                    //varible.Clear();
                    CheckStatment(path, item.Statements);
                    chekable.clearObjectFromBatche();
                    chekable.PostBatcheChecable();
                }
                chekable.clearObjectFromFile();
                chekable.PostFileChecable();

                open.Close();

                //foreach (var message in chekable.messages.Messages.OrderBy(c=>c.Format?.StartLine))
                //{
                //    switch (message.Text.Type)
                //    {
                //        case TypeMessage.Warning:
                //            Console.ForegroundColor = ConsoleColor.Yellow;
                //            break;
                //        case TypeMessage.Error:
                //            Console.ForegroundColor = ConsoleColor.Red;
                //            break;
                //        case TypeMessage.Debug:
                //            Console.ForegroundColor = ConsoleColor.Gray;
                //            break;
                //        default:
                //            break;
                //    }
                //    Console.WriteLine(message.MessageInformation);
                //    Console.ResetColor();
                //}
                //Console.ReadLine();
            }
            return chekable.messages.Messages;
        }

        private void CheckStatment(string path, IList<TSqlStatement> statements)
        {
            foreach (var statement in statements)
            {
                if (statement == null) continue;

                if (statement is CreateFunctionStatement)
                {
                    chekable.getCreateFunctionStatement(statement as CreateFunctionStatement);
                }
                else
                if (statement is UseStatement)
                {
                    chekable.getUseStatement(statement as UseStatement);
                }
                else
                if (statement is AlterTableAddTableElementStatement)
                {
                    chekable.getAlterTableAddTableElementStatement(statement as AlterTableAddTableElementStatement);
                }
                else
                if(statement is UpdateStatement)
                {
                    chekable.getUpdateStatement(statement as UpdateStatement);
                }
                else
                if (statement is CreateTableStatement)
                {
                    chekable.getCreateTableStatement(statement as CreateTableStatement);
                }
                else
                if (statement is CreateProcedureStatement)
                {
                    chekable.getCreateProcedureStatement(statement as CreateProcedureStatement);
                    CheckStatment(path, (statement as CreateProcedureStatement).StatementList.Statements);
                    chekable.PostAllStatmentChecable();
                    chekable.clearObjectFromStatement();
                }
                else
                if (statement is AlterProcedureStatement)
                {
                    chekable.getAlterProcedureStatement(statement as AlterProcedureStatement);
                    CheckStatment(path, (statement as AlterProcedureStatement).StatementList.Statements);
                    chekable.PostAllStatmentChecable();
                    chekable.clearObjectFromStatement();
                }
                else
                if (statement is WhileStatement)
                {
                    CheckStatment(path, new[] { (statement as WhileStatement).Statement });
                }
                else
                if (statement is IfStatement)
                {
                    var ifStatement = statement as IfStatement;
                    
                    if (ifStatement.Predicate is BooleanExpression)
                    {
                        chekable.checkedBooleanComparison(ifStatement.Predicate as BooleanExpression);
                    }

                    CheckStatment(path, new[] { ifStatement.ThenStatement });
                    chekable.clearObjectFromStatement();
                    CheckStatment(path, new[] { ifStatement.ElseStatement });
                    chekable.clearObjectFromStatement();
                }
                else
                if (statement is BeginEndBlockStatement)
                {
                    CheckStatment(path, (statement as BeginEndBlockStatement).StatementList.Statements);
                }
                else
                if (statement is ProcedureStatementBodyBase)
                {
                    CheckStatment(path, (statement as ProcedureStatementBodyBase).StatementList.Statements);
                }
                else
                if (statement is SetVariableStatement)
                {
                    chekable.getSetVariableStatement(statement as SetVariableStatement);
                }
                else
                if (statement is DeclareVariableStatement)
                {
                    chekable.getDeclareVariableStatement(statement as DeclareVariableStatement);
                    //chekable.clearObjectFromStatement();
                }
                else
                if (statement is DeclareTableVariableStatement)
                {
                    chekable.getDeclareTableVariableStatement(statement as DeclareTableVariableStatement);
                    //chekable.clearObjectFromStatement();
                }
                else
                if (statement is SelectStatement)
                {
                    //SaveToFile(path, statement);
                    chekable.getSelectStatement(statement as SelectStatement);
                }
                else
                if (statement is InsertStatement)
                {
                    chekable.getInsertStatement(statement as InsertStatement);
                    chekable.clearObjectFromStatement();
                }
                else
                if (statement is DropTableStatement)
                {
                    chekable.getDropTableStatement(statement as DropTableStatement);
                }
                //else
                //{
                //    SaveToFile(path, statement);
                //}

                //chekable.clearObjectFromStatement();
            }
        }

        

        private void SaveToFileParseError(string path, string v)
        {
            string Name = "_ParseError_" + Path.GetFileName(path);
            int i = 1;
            string FullPathResult = "";
            do
            {
                FullPathResult = Path.Combine(pathresult, Name + "_" + (i++) + ".json");
            } while (File.Exists(FullPathResult));

            File.WriteAllText(FullPathResult, path + "\r\n\r\n" + v, Encoding.Default);
        }

        void SaveToFile(string path, TSqlStatement statement)
        {
            string sereal = "";
            var type = statement.GetType();
            string name = type.Name;
            try
            {
                var j = JObject.FromObject(statement);
                foreach (var item in ondeleteNode)
                {
                    j[item.Key].Parent.Remove();
                }
                removeall(j);

                JsonSerializerSettings setting = new JsonSerializerSettings();
                sereal = j.ToString();
            }
            catch (Exception ex)
            {
                sereal = string.Format("Exception - {0}\r\n\r\n StackTrace - {1}", ex.Message, ex.StackTrace);
                name = "_Exception_" + name;
            }
            int i = 1;
            string FullPathResult = "";
            do
            {
                FullPathResult = Path.Combine(pathresult, name + "_" + (i++) + ".json");
            } while (File.Exists(FullPathResult));

            File.WriteAllText(FullPathResult, path + "\r\n\r\n" + sereal, Encoding.Default);
        }

        private void removeall(JObject j)
        {
            foreach (var node in j.Values())
            {
                WalkNode(node, n =>
                {


                    foreach (var item in ondeleteNode)
                    {
                        JToken token = n[item.Key];
                        if (token != null && token.Type == item.Value)
                        {
                            token.Parent.Remove();
                        }
                    }
                });
            }
        }

        void WalkNode(JToken node, Action<JObject> action)
        {
            if (node.Type == JTokenType.Object)
            {
                action((JObject)node);

                foreach (JProperty child in node.Children<JProperty>())
                {
                    WalkNode(child.Value, action);
                }
            }
            else if (node.Type == JTokenType.Array)
            {
                foreach (JToken child in node.Children())
                {
                    WalkNode(child, action);
                }
            }
        }
    }
}

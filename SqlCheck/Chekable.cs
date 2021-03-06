﻿using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlCheck.Modele;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlCheck
{
    public class Chekable
    {
        bool IsIgnoreCase = true;

        StringComparer caseParam;
        string database = "";
        string schema = "dbo";
        string serverName = "";
        bool IsAliasAll = true;
        public Message messages = new Message();
        Server server;

        Dictionary<string, MyTable> TableFromServer;
        Dictionary<string, ReferCount<DeclareVariableElement, int>> varible;
        Dictionary<string, ReferCount<ProcedureParameter, int>> parametrs;
        Dictionary<string, ReferCount<SchemaObjectName, int>> dropTebles;
        Dictionary<string, ReferCount<FunctionCall, int>> compareNull;
        Dictionary<string, ReferCount<CreateTableStatement, int>> tempTeble;
        Dictionary<string, ReferCount<CreateTableStatement, int>> createTebles;
        Dictionary<string, ReferCount<DeclareTableVariableBody, int>> tableVarible;
        Dictionary<string, ReferCount<TableReference, int>> tables;
        Dictionary<string, ReferCount<CommonTableExpression, int>> withTables;

        List<string> AlterTables = new List<string>();

        MyObjectFromServer GetObjectFromServer(TSqlFragment type)
        {
            if (!server.ConnectionContext.IsOpen) return null;

            if (type is NamedTableReference)
            {
                var t = type as NamedTableReference;
                string fullNameTable = getNameTable(t);
                if (TableFromServer.ContainsKey(fullNameTable))
                {
                    return TableFromServer[fullNameTable];
                }
                var table = t.SchemaObject.BaseIdentifier.Value;
                var schema = t.SchemaObject.SchemaIdentifier?.Value ?? this.schema;
                var databaseNow = t.SchemaObject.DatabaseIdentifier?.Value ?? database;
                Database db = server.Databases[databaseNow];
                SqlSmoObject obj = db.Tables[table, schema] as SqlSmoObject ?? db.Views[table, schema] as SqlSmoObject;
                var temptable = new MyTable();
                if (obj != null)
                {
                    if (obj is TableViewTableTypeBase)
                    {
                        var tableServer = obj as TableViewTableTypeBase;
                        temptable.Name = tableServer.ToString();
                        temptable.IsExists = true;
                        foreach (Column column in tableServer.Columns)
                        {
                            temptable.AddColumns(column);
                        }
                        TableFromServer.Add(fullNameTable, temptable);
                    }
                    else
                    {
                        messages.addMessage(Code.T0000022, type, fullNameTable, obj.GetType().Name);
                    }
                }
                else
                {
                    messages.addMessage(Code.T0000023, type, fullNameTable);
                    TableFromServer.Add(fullNameTable, temptable);
                }
                return temptable;
            }
            return null;
        }
        public Chekable()
        {
            server = new Server();

            if (IsIgnoreCase)
            {
                caseParam = StringComparer.OrdinalIgnoreCase;
            }
            else
            {
                caseParam = null;
            }
            TableFromServer = new Dictionary<string, MyTable>(caseParam);
            varible = new Dictionary<string, ReferCount<DeclareVariableElement, int>>(caseParam);
            parametrs = new Dictionary<string, ReferCount<ProcedureParameter, int>>(caseParam);
            dropTebles = new Dictionary<string, ReferCount<SchemaObjectName, int>>(caseParam);
            compareNull = new Dictionary<string, ReferCount<FunctionCall, int>>(caseParam);
            tempTeble = new Dictionary<string, ReferCount<CreateTableStatement, int>>(caseParam);
            createTebles = new Dictionary<string, ReferCount<CreateTableStatement, int>>(caseParam);
            tableVarible = new Dictionary<string, ReferCount<DeclareTableVariableBody, int>>(caseParam);
            tables = new Dictionary<string, ReferCount<TableReference, int>>(caseParam);
            withTables = new Dictionary<string, ReferCount<CommonTableExpression, int>>(caseParam);


        }
        public Chekable(string ConnectionString) : this()
        {
            try
            {
                server.ConnectionContext.ConnectionString = ConnectionString;
                server.ConnectionContext.Connect();
                if (server.ConnectionContext.IsOpen)
                {
                    var conect = (server.ConnectionContext.SqlConnectionObject as SqlConnection);
                    serverName = conect.WorkstationId;
                    database = conect.Database;
                    schema = server.Databases[database].DefaultSchema;
                }
            }
            catch (Exception ex)
            {
                string pref = "Не удалось выполнить подключение";
                if (server.ConnectionContext.IsOpen)
                    pref = "Ошибка сервера - {0}; ConnectionString - '{1}';";

                Console.WriteLine($"{pref} - {ex.Message}; ConnectionString - '{ConnectionString}';");
            }
        }


        #region Statements
        public void getCreateFunctionStatement(CreateFunctionStatement createFunctionStatement)
        {
            if (createFunctionStatement.ReturnType != null)
            {
                if (createFunctionStatement.ReturnType is TableValuedFunctionReturnType)
                {
                    var t = createFunctionStatement.ReturnType as TableValuedFunctionReturnType;
                    tableVarible.Add(t.DeclareTableVariableBody.VariableName.Value, new ReferCount<DeclareTableVariableBody, int>(t.DeclareTableVariableBody, 0));
                }
            }
        }
        public void getUseStatement(UseStatement useStatement)
        {
            this.database = useStatement.DatabaseName.Value;
        }
        public void getAlterTableAddTableElementStatement(AlterTableAddTableElementStatement alterTableAddTableElementStatement)
        {
            if (!AlterTables.Any(c => string.Compare(c, alterTableAddTableElementStatement.SchemaObjectName.BaseIdentifier.Value, IsIgnoreCase) == 0))
            {
                AlterTables.Add(alterTableAddTableElementStatement.SchemaObjectName.BaseIdentifier.Value);
            }
        }
        public void getUpdateStatement(UpdateStatement updateStatement)
        {
            if (updateStatement.UpdateSpecification.Target is NamedTableReference)
            {
                var target = updateStatement.UpdateSpecification.Target as NamedTableReference;

                MyTable table = null;
                if (updateStatement.UpdateSpecification.FromClause != null)
                {
                    foreach (var tableReference in updateStatement.UpdateSpecification.FromClause.TableReferences)
                    {
                        CheckeTableReference(tableReference);
                    }
                    //var table = GetMyTable(target, true);
                    ///предположительно target должен быть alias на таблицу из FromClause

                    string al = getAliasOrNameTable(target, false);
                    var ta = getTableFromAlias(al, target, false) as NamedTableReference;
                    if (ta == null)
                    {
                        ta = getTableFromAlias(target.SchemaObject.BaseIdentifier.Value, target, false) as NamedTableReference;
                        if (ta == null)
                        {
                            string targetName = getNameTable(target);
                            foreach (var t in tables)
                            {
                                if (t.Value.Obj is NamedTableReference)
                                {
                                    var tableObj = t.Value.Obj as NamedTableReference;
                                    string tableName = getNameTable(tableObj);

                                    if (string.Compare(targetName, tableName, IsIgnoreCase) == 0)
                                    {
                                        ta = tableObj;

                                        messages.addMessage(Code.T0000052, target, t.Key);

                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (ta != null)
                    {
                        table = GetMyTable(ta, true);
                    }
                    else
                    {
                        messages.addMessage(Code.T0000050, target, al);
                    }
                }
                else
                {
                    IsAliasAll = false;
                    table = GetMyTable(target, true);
                }

                if (updateStatement.UpdateSpecification.WhereClause != null)
                {
                    //updateStatement.UpdateSpecification.WhereClause.
                    checkedBooleanComparison(updateStatement.UpdateSpecification.WhereClause.SearchCondition);
                }

                ///checked column set
                foreach (var set in updateStatement.UpdateSpecification.SetClauses)
                {
                    if (set is AssignmentSetClause)
                    {
                        if (table != null && table.IsExists)
                        {
                            ///текущую колонку мы можем проверить, только при условии получили структуру таблицы
                            ///иначе мы можем проверить только чем обновляем
                            var column = (set as AssignmentSetClause).Column;
                            if (column is ColumnReferenceExpression)
                            {
                                var myColumn = table.getColumn(column);
                                if (myColumn != null)
                                {
                                    var newVal = (set as AssignmentSetClause).NewValue;
                                    if (newVal is ColumnReferenceExpression)
                                    {
                                    }
                                    if (newVal is Literal)
                                    {
                                        //сравнить типы (если возможно)
                                    }
                                }
                                else
                                {
                                    messages.addMessage(Code.T0000049, column, getNameIdentifiers(column.MultiPartIdentifier), table.Name);
                                }
                            }
                        }
                        else
                        {
                            var newVal = (set as AssignmentSetClause).NewValue;
                            if (newVal is ColumnReferenceExpression)
                            {
                                checkedColumnReference(newVal as ColumnReferenceExpression);
                            }
                        }
                    }
                }

                if (AlterTables.Any(c => string.Compare(c, target.SchemaObject.BaseIdentifier.Value, IsIgnoreCase) == 0))
                {
                    messages.addMessage(Code.T0000046, updateStatement, target.SchemaObject.BaseIdentifier.Value);
                }
            }
        }
        public void getCreateProcedureStatement(CreateProcedureStatement createProcedureStatement)
        {
            foreach (var param in createProcedureStatement.Parameters)
            {
                parametrs.Add(param.VariableName.Value, new ReferCount<ProcedureParameter, int>(param, 0));
            }
        }
        public void getAlterProcedureStatement(AlterProcedureStatement createProcedureStatement)
        {
            foreach (var param in createProcedureStatement.Parameters)
            {
                parametrs.Add(param.VariableName.Value, new ReferCount<ProcedureParameter, int>(param, 0));
            }
        }
        public void getDropTableStatement(DropTableStatement dropTableStatement)
        {
            foreach (var item in dropTableStatement.Objects)
            {
                if (item is SchemaObjectName)
                {
                    string table = getNameIdentifiers((item as SchemaObjectName));
                    if (!compareNull.ContainsKey(table))
                    {
                        messages.addMessage(Code.T0000044, dropTableStatement, table);
                    }

                    dropTebles.Add(table, new ReferCount<SchemaObjectName, int>(item, 0));
                }
            }
        }
        public void getCreateTableStatement(CreateTableStatement createTableStatement)
        {
            var ident = getNameIdentifiers(createTableStatement.SchemaObjectName);
            if (IsTempTable(createTableStatement.SchemaObjectName.BaseIdentifier.Value))
            {
                if (tempTeble.ContainsKey(ident))
                {
                    messages.addMessage(Code.T0000039, createTableStatement, ident);
                    return;
                }
                else
                {
                    tempTeble.Add(ident, new ReferCount<CreateTableStatement, int>(createTableStatement, 0));
                }
            }
            else
            {
                if (createTebles.ContainsKey(ident))
                {
                    messages.addMessage(Code.T0000040, createTableStatement, ident);
                    return;
                }
                else
                {
                    if (!dropTebles.ContainsKey(ident))
                    {
                        messages.addMessage(Code.T0000041, createTableStatement, ident);
                    }

                    createTebles.Add(ident, new ReferCount<CreateTableStatement, int>(createTableStatement, 0));
                }
            }
            //SchemaObjectName
        }
        public void getDeclareTableVariableStatement(DeclareTableVariableStatement declareTableVar)
        {
            if (tableVarible.ContainsKey(declareTableVar.Body.VariableName.Value))
            {
                messages.addMessage(Code.T0000038, declareTableVar, declareTableVar.Body.VariableName.Value);
                return;
            }
            tableVarible.Add(declareTableVar.Body.VariableName.Value, new ReferCount<DeclareTableVariableBody, int>(declareTableVar.Body, 0));
        }
        public void getInsertStatement(InsertStatement statement)
        {
            string target = "";
            bool isTargetValidate = false;
            List<ColumnDefinition> columns = new List<ColumnDefinition>();
            MyTable table = null;
            if (statement.InsertSpecification.Target is NamedTableReference)
            {
                var Target = statement.InsertSpecification.Target as NamedTableReference;
                CheckeTableReference(Target, false);
                target = getNameTable(Target);
                if (!IsTempTable(target))
                {
                    table = GetObjectFromServer(Target) as MyTable;
                }
                //columns = getSpecificationTableTarget(Target);
                isTargetValidate = columns != null && columns.Count > 0;
            }
            if (statement.InsertSpecification.Target is VariableTableReference)
            {
                var body = getDeclareTableVariable(statement.InsertSpecification.Target as VariableTableReference);
                if (body != null)
                {
                    columns = body.Definition.ColumnDefinitions.ToList();
                    target = body.VariableName.Value;
                }
            }
            if (statement.InsertSpecification.Columns.Count == 0)
            {
                messages.addMessage(Code.T0000007, statement, target);
            }
            if (statement.InsertSpecification.InsertSource is SelectInsertSource)
            {
                var select = statement.InsertSpecification.InsertSource as SelectInsertSource;
                if (select.Select is QuerySpecification)
                {
                    var query = select.Select as QuerySpecification;
                    if (query.SelectElements.Count > statement.InsertSpecification.Columns.Count)
                    {
                        messages.addMessage(Code.T0000009, statement, target);
                    }
                    else
                    if (query.SelectElements.Count < statement.InsertSpecification.Columns.Count)
                    {
                        messages.addMessage(Code.T0000010, statement, target);
                    }
                    GetQuerySpecification(query);

                    isTargetValidate = isTargetValidate && columns.Count > 0;
                    for (int i = 0; i < query.SelectElements.Count; i++)
                    {
                        var element = query.SelectElements[i];
                        if (isTargetValidate)
                        {
                            var column = columns[i];
                        }
                        if (element is SelectScalarExpression)
                        {
                            var expression = (element as SelectScalarExpression).Expression;
                            if (expression is Literal)
                            {

                            }
                            if (expression is ColumnReferenceExpression)
                            {
                                //checkedColumnReference(expression as ColumnReferenceExpression);
                            }
                            if (expression is VariableReference)
                            {

                            }
                            if (expression is ScalarSubquery)
                            {
                                GetScalarSubquery(expression as ScalarSubquery);
                            }
                        }
                        if (element is SelectStarExpression)
                        {
                            messages.addMessage(Code.T0000008, element, target);
                        }
                    }
                }
                if (select.Select is BinaryQueryExpression)
                {
                    var query = select.Select as BinaryQueryExpression;
                    //recurse

                }
            }
        }
        public void getSetVariableStatement(SetVariableStatement set)
        {
            var setResult = new SetVariableStatement();

            var var = getDeclare(set.Variable);

            if (set.Expression is BinaryExpression)
            {
                set.Expression = calculateExpression(set.Expression as BinaryExpression);
            }
            setResult.Expression = convertExpression(set.Expression);

            var.Value = setResult.Expression;

            StringChecked(var);
        }
        public void getDeclareVariableStatement(DeclareVariableStatement dec)
        {
            foreach (var declar in dec.Declarations)
            {
                if (varible.ContainsKey(declar.VariableName.Value))
                {
                    messages.addMessage(Code.T0000003, declar, declar.VariableName.Value);
                    continue;
                }
                varible.Add(declar.VariableName.Value, new ReferCount<DeclareVariableElement, int>(CloneObject(declar) as DeclareVariableElement, 0));
                StringChecked(declar);
            }
        }
        public void getSelectStatement(SelectStatement select)
        {
            if (select.WithCtesAndXmlNamespaces != null && select.WithCtesAndXmlNamespaces is WithCtesAndXmlNamespaces)
            {
                foreach (var item in select.WithCtesAndXmlNamespaces.CommonTableExpressions)
                {
                    GetQuerySpecification(item.QueryExpression as QuerySpecification, IsAddTableList: false);
                    addWithTable(item);
                }
            }
            if (select.QueryExpression != null && select.QueryExpression is QuerySpecification)
            {
                GetQuerySpecification(select.QueryExpression as QuerySpecification);

            }
        }

        //void checkedSearchCondition(BooleanExpression searchCondition)
        //{
        //    if (searchCondition is BooleanBinaryExpression)
        //    {
        //        var search = searchCondition as BooleanBinaryExpression;
        //        checkedBooleanComparison(search);
        //    }
        //    if (searchCondition is BooleanComparisonExpression)
        //    {
        //        var search = searchCondition as BooleanComparisonExpression;
        //        checkedBooleanComparison(search);
        //    }
        //}
        #endregion

        #region public
        public void clearObjectFromStatement()
        {
            IsAliasAll = true;
            tables.Clear();
            withTables.Clear();
            parametrs.Clear();
            compareNull.Clear();
            dropTebles.Clear();
        }
        public void clearObjectFromFile()
        {
            tempTeble.Clear();
        }
        public void clearObjectFromBatche()
        {
            AlterTables.Clear();
            varible.Clear();
            tableVarible.Clear();
        }
        public void CheckUsengTableVarible()
        {
            foreach (var table in tableVarible)
            {
                if (table.Value.Count == 0)
                {
                    messages.addMessage(Code.T0000011, table.Value.Obj, table.Key);
                }
            }
        }
        public void CheckUsingVariable()
        {
            foreach (var value in varible)
            {
                if (value.Value.Count == 0)
                {
                    messages.addMessage(Code.T0000001, value.Value.Obj, value.Value.Obj.VariableName.Value);
                }
            }
        }
        public void CheckUsingParams()
        {
            foreach (var parametr in parametrs)
            {
                if (parametr.Value.Count == 0)
                {
                    messages.addMessage(Code.T0000045, parametr.Value.Obj, parametr.Value.Obj.VariableName.Value);
                }
            }
        }
        public void PostFileChecable()
        {
            ///проверка удаления временных таблиц            
            ///
            //использование постоянной таблицы
            //foreach (var ctable in createTebles)
            //{
            //    if(ctable.Value.Count == 0)
            //    {
            //        messages.addMessage(Code.T0000047, ctable.Value.Obj, ctable.Key);
            //    }
            //}
        }
        public void PostBatcheChecable()
        {
            ///проверка закрытия транзакций
            ///проверка использвание переменных
            ///проверка использвание аргументов
            CheckUsingVariable();
            CheckUsengTableVarible();
        }
        public void PostAllStatmentChecable()
        {
            CheckUsingVariable();
            CheckUsingParams();
        }
        public bool checkedBooleanComparison(BooleanExpression booleanExpression)
        {
            if (booleanExpression is BooleanBinaryExpression)
            {
                bool IsFirst = checkedBooleanComparison((booleanExpression as BooleanBinaryExpression).FirstExpression);
                bool Second = checkedBooleanComparison((booleanExpression as BooleanBinaryExpression).SecondExpression);
                return IsFirst && Second;
            }
            if (booleanExpression is BooleanComparisonExpression)
            {
                return checkedBooleanComparisonExpression(booleanExpression as BooleanComparisonExpression);
            }
            if (booleanExpression is BooleanIsNullExpression)
            {
                return checkedBooleanIsNullExpression(booleanExpression as BooleanIsNullExpression);
            }
            if (booleanExpression is ExistsPredicate)
            {
                return true;
            }
            if (booleanExpression is InPredicate)
            {
                if ((booleanExpression as InPredicate).Expression is ColumnReferenceExpression)
                {
                    checkedColumnReference((booleanExpression as InPredicate).Expression as ColumnReferenceExpression);
                }
                if ((booleanExpression as InPredicate).Subquery is ScalarSubquery)
                {
                    GetScalarSubquery((booleanExpression as InPredicate).Subquery as ScalarSubquery);
                }
            }
            return true;
        }
        #endregion

        #region Table

        MyTable GetMyTable(TableReference tableReference, bool isGetColumn = false)
        {
            MyTable myTable = new MyTable(getNameTable(tableReference));

            if (myTable.Name == null) return null;

            var tableFromCreate = GetTableFromCreate(myTable.Name);
            myTable.IsExists = tableFromCreate != null;
            if (myTable.IsExists)
            {
                if (isGetColumn)
                {
                    foreach (var column in tableFromCreate.Definition.ColumnDefinitions)
                    {
                        myTable.AddColumns(column);
                    }
                }
                return myTable;
            }
            myTable = GetObjectFromServer(tableReference) as MyTable;
            return myTable;
        }
        void CheckeTableReference(TableReference tableReference, bool isAdd = true)
        {
            if (tableReference is QualifiedJoin)
            {
                var join = tableReference as QualifiedJoin;
                //bool IsFirst = 
                CheckeTableReference(join.FirstTableReference);
                //bool Second = 
                CheckeTableReference(join.SecondTableReference);
                //bool isCompare = 
                checkedBooleanComparison(join.SearchCondition);
                //return isCompare && IsFirst && Second;
            }
            if (tableReference is NamedTableReference)
            {
                //проверить результат
                if (IsTempTable((tableReference as NamedTableReference).SchemaObject.BaseIdentifier.Value))
                {
                    var table = GetTempTable((tableReference as NamedTableReference).SchemaObject);
                }
                else
                {
                    ////
                }
                if (isAdd)
                    AddTable(tableReference);
            }
            if (tableReference is VariableTableReference)
            {
                //проверить результат
                getDeclareTableVariable(tableReference as VariableTableReference);

                if (isAdd)
                    AddTable(tableReference);
            }
            if (tableReference is QueryDerivedTable)
            {
                var query = tableReference as QueryDerivedTable;
                lastderivedTable = query.Alias.Value;
                derivedTables.Push(new List<MyTable>() { new MyTable(lastderivedTable) });
                ///пока не понятно когда очищать этот список

                GetQuerySpecification(query.QueryExpression as QuerySpecification, derivedTables.Peek());
            }
            if (tableReference is SchemaObjectFunctionTableReference)
            {
                var t = tableReference as SchemaObjectFunctionTableReference;

                if (t.Alias == null)
                {
                    IsAliasAll = false;
                }

                foreach (var param in t.Parameters)
                {
                    if (param is VariableReference)
                    {
                        var r = getDeclare(param as VariableReference);
                    }
                }
            }
        }
        CreateTableStatement GetTempTable(MultiPartIdentifier dif)
        {
            string name = getNameIdentifiers(dif);
            if (!tempTeble.ContainsKey(name))
            {
                messages.addMessage(Code.T0000016, dif, name);
                return null;
            }

            tempTeble[name].Count++;
            return tempTeble[name].Obj;
        }
        CreateTableStatement GetTableFromCreate(string table)
        {
            if (tempTeble.ContainsKey(table))
            {
                tempTeble[table].Count++;
                return tempTeble[table].Obj;
            }
            if (createTebles.ContainsKey(table))
            {
                createTebles[table].Count++;
                return createTebles[table].Obj;
            }
            return null;
        }
        TSqlFragment getTableFromAlias(string alias, TSqlFragment fragment, bool isSendMessage = true)
        {
            bool T = tables.ContainsKey(alias);
            bool W = withTables.ContainsKey(alias);
            if (!T && !W)
            {
                if (isSendMessage)
                    messages.addMessage(Code.T0000014, fragment, alias);
            }
            else
            if (T)
            {
                tables[alias].Count++;
                return tables[alias].Obj;
            }
            else
            if (W)
            {
                withTables[alias].Count++;
                return withTables[alias].Obj;
            }
            return null;
        }
        void getTableServerFromAlias(string firstAlias)
        {
            throw new NotImplementedException();
        }
        DeclareTableVariableBody getDeclareTableVariable(VariableTableReference vtr)
        {
            if (!tableVarible.ContainsKey(vtr.Variable.Name))
            {
                messages.addMessage(Code.T0000015, vtr, vtr.Variable.Name);
                return null;
            }
            tableVarible[vtr.Variable.Name].Count++;
            return tableVarible[vtr.Variable.Name].Obj;
        }
        string getNameTable(TableReference table)
        {
            if (table is NamedTableReference)
            {
                var named = table as NamedTableReference;
                return getNameIdentifiers(named.SchemaObject);
            }
            return null;
        }
        string getAliasTable(TableReference table)
        {
            if (table is NamedTableReference)
            {
                return (table as NamedTableReference)?.Alias?.Value;
            }
            if (table is VariableTableReference)
            {
                return (table as VariableTableReference)?.Alias?.Value;
            }
            if (table is QueryDerivedTable)
            {
                return (table as QueryDerivedTable)?.Alias?.Value;
            }
            return null;
        }
        string getAliasOrNameTable(TableReference table, bool chekedAlias = true)
        {
            string key = getAliasTable(table);
            if (key == null)
            {
                if (chekedAlias)
                    IsAliasAll = false;
                key = getNameTable(table);
            }
            return key;
        }
        void addWithTable(CommonTableExpression with)
        {
            string key = with.ExpressionName.Value;
            if (withTables.ContainsKey(key))
            {
                messages.addMessage(Code.T0000018, with, key);
                return;
            }

            withTables.Add(key, new ReferCount<CommonTableExpression, int>(with, 0));
        }
        void AddTable(TableReference tableReference)
        {
            string key = getAliasOrNameTable(tableReference);
            if (tables.ContainsKey(key))
            {
                messages.addMessage(Code.T0000017, tableReference, key, getNameTable(tables[key].Obj));
                return;
            }
            tables.Add(key, new ReferCount<TableReference, int>(tableReference, 0));
        }
        bool IsTempTable(string name)
        {
            return name.Length > 0 && name[0] == '#'
                || (name.Length > 1 && name[0] == '#' && name[1] == '#');
        }
        #endregion

        #region Column
        MyColumn getMyColumn(string alias, string columnName, TSqlFragment fragment)
        {
            MyColumn column = null;
            column = getColumnFromDerivedTable(alias, columnName, fragment);
            if (column != null) return column;
            if (alias != null)
            {
                var table = getTableFromAlias(alias, fragment);
                if (table is NamedTableReference)
                {
                    var myTable = GetMyTable(table as NamedTableReference, true);
                    if (myTable != null && myTable.IsExists)
                    {
                        column = myTable.getColumn(columnName);
                        if (column != null)
                        {
                            messages.addMessage(Code.T0000029, table, (table as NamedTableReference).SchemaObject.BaseIdentifier.Value, columnName);
                        }
                    }
                }
                if (table is VariableTableReference)
                {
                    DeclareTableVariableBody myTable = getDeclareTableVariable(table as VariableTableReference);
                    if (myTable != null)
                    {
                        foreach (var col in myTable.Definition.ColumnDefinitions)
                        {
                            if (col is ColumnDefinition)
                            {
                                var c = col as ColumnDefinition;
                                if (string.Compare(c.ColumnIdentifier.Value, columnName, IsIgnoreCase) == 0)
                                {
                                    column = new MyColumn(c);
                                    break;
                                }
                            }
                        }
                        if (column == null)
                        {
                            messages.addMessage(Code.T0000029, table, (table as VariableTableReference).Variable.Name, columnName);
                        }
                    }
                }
            }
            else
            {
                List<MyColumn> cols = new List<MyColumn>();
                bool IsSendMessage = false;
                foreach (var table in tables)
                {
                    var myTable = GetMyTable(table.Value.Obj, true);

                    if (myTable != null && myTable.IsExists)
                    {
                        IsSendMessage = true;
                        MyColumn myColumn = myTable.getColumn(columnName);
                        if (myColumn != null)
                            cols.Add(myColumn);

                        if (cols.Count > 1)
                            break;
                    }
                }
                if (cols.Count == 1)
                {
                    column = cols[0];
                }
                if (cols.Count > 1)
                {
                    messages.addMessage(Code.T0000033, fragment, columnName);
                }
                if (IsSendMessage && cols.Count == 0)
                    messages.addMessage(Code.T0000030, fragment, columnName);
            }
            return column;
        }

        private MyColumn getColumnFromDerivedTable(string alias, string columnName, TSqlFragment fragment)
        {
            MyColumn column = null;
            var derTables = derivedTables.Any() ? derivedTables.Peek() : null;
            if (derTables == null) return null;
            if (alias != null)
            {
                var table = derTables.SingleOrDefault(c => c.Name == alias);
                if (table != null)
                    return table.getColumn(columnName);
            }
            else
            {
                List<MyColumn> cols = new List<MyColumn>();
                bool IsSendMessage = false;
                foreach (var table in derTables)
                {
                    MyColumn myColumn = table.getColumn(columnName);
                    if (myColumn != null)
                        cols.Add(myColumn);

                    if (cols.Count > 1)
                        break;

                }
                if (cols.Count == 1)
                {
                    column = cols[0];
                }
                if (cols.Count > 1)
                {
                    messages.addMessage(Code.T0000033, fragment, columnName);
                }
                if (IsSendMessage && cols.Count == 0)
                    messages.addMessage(Code.T0000030, fragment, columnName);
            }
            return column;
        }
        MyColumn checkedColumnReference(ColumnReferenceExpression Expression)
        {
            var column = new MyColumn((Column)null);
            var Identifiers = Expression.MultiPartIdentifier.Identifiers;
            column.Alias = Identifiers.Count > 1 ? Identifiers[Identifiers.Count - 2].Value : null;
            column.Name = Identifiers[Identifiers.Count - 1].Value;

            column.IsValid = !(Identifiers.Count > 2);

            if (!column.IsValid)
            {
                messages.addMessage(Code.T0000027, Expression, getNameIdentifiers(Expression.MultiPartIdentifier));
            }
            if (IsAliasAll && column.Alias == null)
            {
                messages.addMessage(Code.T0000019, Expression, column.Name);
            }
            //var HasColumnFromCreateTable = findColumnFromLocalTemp(column);
            //!HasColumnFromCreateTable &&
            if (column.IsValid)
            {
                column.Column = getMyColumn(column.Alias, column.Name, Expression)?.Column;
            }
            return column;
        }
        Stack<List<MyTable>> derivedTables = new Stack<List<MyTable>>();
        string lastderivedTable = "";
        bool findColumnFromLocalTemp(MyColumn myColumn)
        {
            foreach (var table in createTebles)
            {
                foreach (var column in table.Value.Obj.Definition.ColumnDefinitions)
                {
                    if (string.Compare(myColumn.Name, column.ColumnIdentifier.Value, IsIgnoreCase) == 0)
                    {
                        //column.DataType
                        //myColumn.Column.DataType;
                        return true;
                    }
                }
            }

            //messages.addMessage
            return false;
        }
        void checkColumnType(Column firstColumn, Column secondColumn)
        {
#warning включить проверки по типам
            if (firstColumn != null && secondColumn != null)
            {
                if (firstColumn.DataType != secondColumn.DataType)
                {
                    messages.addMessage(new MyTyps("типы для таблиц не равны!", TypeMessage.Error), null);
                }
            }
        }
        #endregion
        void GetQuerySpecification(QuerySpecification Query, List<MyTable> myTableList = null, bool IsAddTableList = true)
        {
            if (Query.FromClause != null)
            {
                var from = Query.FromClause as FromClause;
                foreach (TableReference tableReference in from.TableReferences)
                {
                    CheckeTableReference(tableReference);
                }
            }
            if (Query.OrderByClause != null)
            {
                foreach (var element in Query.OrderByClause.OrderByElements)
                {
                    if (element.Expression is Literal)
                    {
                        messages.addMessage(Code.T0000051, element.Expression);
                    }
                }
            }
            foreach (var element in Query.SelectElements)
            {
                if (element is SelectScalarExpression)
                {
                    var expression = element as SelectScalarExpression;
                    if (expression.Expression is VariableReference)
                    {
                        getDeclare(expression.Expression as VariableReference);
                    }
                    if (expression.Expression is ColumnReferenceExpression)
                    {
                        var myColumn = checkedColumnReference(expression.Expression as ColumnReferenceExpression);
                        if (myTableList != null)
                        {
                            var lastTable = myTableList.Single(c => c.Name == lastderivedTable);
                            if (expression.ColumnName != null)
                            {
                                myColumn.Name = expression.ColumnName.Value;
                            }
                            lastTable.AddColumns(myColumn);
                        }
                    }
                    if (expression.Expression is FunctionCall)
                    {
                        getResultFunctionCall(expression.Expression as FunctionCall);

                    }
                }
            }
            if (Query.WhereClause != null)
            {
                checkedBooleanComparison(Query.WhereClause.SearchCondition);
            }
        }
        void StringChecked(DeclareVariableElement var)
        {
            if (var.Value is StringLiteral && var.DataType is SqlDataTypeReference)
            {
                StringLiteral stringLiteral = var.Value as StringLiteral;
                var DataType = var.DataType as SqlDataTypeReference;

                if ((DataType.SqlDataTypeOption == SqlDataTypeOption.NVarChar
                        || DataType.SqlDataTypeOption == SqlDataTypeOption.VarChar)
                    && stringLiteral != null && stringLiteral.Value != null
                    )
                {
                    if (string.Compare(DataType.Parameters[0].Value, "max", true) == 0)
                    {
                        DataType.Parameters[0].Value = "8000";
                    }
                    int len = int.Parse(DataType.Parameters[0].Value);
                    if (len < stringLiteral.Value.Length)
                    {
                        messages.addMessage(Code.T0000002, var, var.VariableName.Value);
                    }
                }
            }
            if (var.Value is ScalarSubquery)
            {
                GetScalarSubquery(var.Value as ScalarSubquery);
            }
        }
        void GetScalarSubquery(ScalarSubquery subquery)
        {
            if (subquery.QueryExpression is QuerySpecification)
            {
                var queryPart = subquery.QueryExpression as QuerySpecification;

                bool isFunc = queryPart.FromClause != null
                        && queryPart.FromClause.TableReferences.Count == 1
                        && queryPart.FromClause.TableReferences[0] is SchemaObjectFunctionTableReference;

                if (queryPart.TopRowFilter != null)
                {
                    Literal literal = null;
                    if (queryPart.TopRowFilter.Expression is ParenthesisExpression)
                    {
                        var p = queryPart.TopRowFilter.Expression as ParenthesisExpression;
                        if (p.Expression is VariableReference)
                        {
                            literal = GetLiteral(p.Expression as VariableReference);
                        }
                        else
                        if (p.Expression is IntegerLiteral)
                        {
                            literal = p.Expression as IntegerLiteral;
                        }
                        else
                        {
                            messages.addMessage(Code.T0000036, queryPart.TopRowFilter.Expression, p.Expression.GetType().Name);
                        }
                    }
                    if (queryPart.TopRowFilter.Expression is Literal)
                    {
                        if (queryPart.TopRowFilter.Expression is IntegerLiteral)
                        {
                            literal = queryPart.TopRowFilter.Expression as IntegerLiteral;
                        }
                        else
                        {
                            messages.addMessage(Code.T0000036, queryPart.TopRowFilter.Expression, queryPart.TopRowFilter.Expression.GetType().Name);
                        }
                    }
                    if (literal != null && int.Parse(literal.Value) != 1)
                    {
                        messages.addMessage(Code.T0000035, subquery, literal.Value);
                    }
                }
                else
                {
                    if (!isFunc && queryPart.FromClause != null)
                        messages.addMessage(Code.T0000034, subquery);
                }

                if (queryPart.WhereClause == null && queryPart.FromClause != null && !isFunc)
                {
                    messages.addMessage(Code.T0000037, subquery);
                }

                GetQuerySpecification(queryPart);

                if (queryPart.SelectElements.Count > 1)
                {
                    messages.addMessage(Code.T0000012, subquery);
                }
                else
                if (queryPart.SelectElements.Count == 1)
                {
                    var el = queryPart.SelectElements[0];
                    if (el is SelectStarExpression)
                    {
                        messages.addMessage(Code.T0000013, subquery);
                        return;
                    }
                    if (el is SelectScalarExpression)
                    {
                        var expression = (el as SelectScalarExpression).Expression;
                        if (expression is Literal)
                        {

                        }
                        if (expression is ColumnReferenceExpression)
                        {

                        }
                        if (expression is VariableReference)
                        {

                        }
                        if (expression is ScalarSubquery)
                        {
                            GetScalarSubquery(expression as ScalarSubquery);
                        }
                    }
                }
            }
        }
        Literal GetLiteral(VariableReference variableReference)
        {
            var res = getDeclare(variableReference).Value;
            if (res is Literal)
            {
                return res as Literal;
            }
            else
            if (res is BinaryExpression)
            {
                res = calculateExpression(res as BinaryExpression);
                //getBooleanBinaryExpression(res as BinaryExpression);
            }
            return res as Literal;
        }
        DeclareVariableElement getDeclare(VariableReference variableReference)
        {
            if (string.IsNullOrEmpty(variableReference.Name))
            {
                throw new Exception(string.Format("Переменная не определена"));
            }
            if (varible.ContainsKey(variableReference.Name))
            {
                varible[variableReference.Name].Count++;
                return varible[variableReference.Name].Obj;
            }
            else
            if (parametrs.ContainsKey(variableReference.Name))
            {
                parametrs[variableReference.Name].Count++;
                return parametrs[variableReference.Name].Obj;
            }
            messages.addMessage(Code.T0000004, variableReference, variableReference.Name);
            return null;
        }
        Literal calculateExpression(BinaryExpression expr)
        {
            if (expr.FirstExpression is BinaryExpression)
            {
                expr.FirstExpression = calculateExpression(expr.FirstExpression as BinaryExpression);
            }

            expr.FirstExpression = convertExpression(expr.FirstExpression);

            expr.SecondExpression = convertExpression(expr.SecondExpression);

            if (expr.FirstExpression is Literal && expr.SecondExpression is Literal)
            {
                if (expr.BinaryExpressionType == BinaryExpressionType.Add)
                {
                    if (expr.FirstExpression is StringLiteral && expr.SecondExpression is StringLiteral)
                        return calculate<StringLiteral, string>(expr, (a) => a, (a, b) => { return a + b; });
                    if (expr.FirstExpression is IntegerLiteral && expr.SecondExpression is IntegerLiteral)
                        return calculate<IntegerLiteral, int>(expr, int.Parse, (a, b) => { return a + b; });
                    if (expr.FirstExpression is NumericLiteral && expr.SecondExpression is NumericLiteral)
                        return calculate<IntegerLiteral, float>(expr, float.Parse, (a, b) => { return a + b; });
                }
                else
                {
                    if (expr.FirstExpression is StringLiteral && expr.SecondExpression is StringLiteral)
                        messages.addMessage(Code.T0000005, expr.FirstExpression, expr.BinaryExpressionType.ToString());
                }
                if (expr.BinaryExpressionType == BinaryExpressionType.Subtract)
                {
                    if (expr.FirstExpression is IntegerLiteral && expr.SecondExpression is IntegerLiteral)
                        return calculate<IntegerLiteral, int>(expr, int.Parse, (a, b) => { return a - b; });
                    if (expr.FirstExpression is NumericLiteral && expr.SecondExpression is NumericLiteral)
                        return calculate<IntegerLiteral, float>(expr, float.Parse, (a, b) => { return a - b; });
                }
                if (expr.BinaryExpressionType == BinaryExpressionType.Multiply)
                {
                    if (expr.FirstExpression is IntegerLiteral && expr.SecondExpression is IntegerLiteral)
                        return calculate<IntegerLiteral, int>(expr, int.Parse, (a, b) => { return a * b; });
                    if (expr.FirstExpression is NumericLiteral && expr.SecondExpression is NumericLiteral)
                        return calculate<IntegerLiteral, float>(expr, float.Parse, (a, b) => { return a * b; });
                }
                if (expr.BinaryExpressionType == BinaryExpressionType.Divide)
                {
                    if (expr.FirstExpression is IntegerLiteral && expr.SecondExpression is IntegerLiteral)
                        return calculate<IntegerLiteral, int>(expr, int.Parse, (a, b) => { return a / b; });
                    if (expr.FirstExpression is NumericLiteral && expr.SecondExpression is NumericLiteral)
                        return calculate<IntegerLiteral, float>(expr, float.Parse, (a, b) => { return a / b; });
                }
            }
            return null;
        }
        Literal getConvertOrCast(ScalarExpression secondExpression)
        {
            dynamic castCall = secondExpression as CastCall;
            if (secondExpression is ConvertCall)
                castCall = secondExpression as ConvertCall;

            if (castCall.Parameter is VariableReference)
            {
                castCall.Parameter = GetLiteral(castCall.Parameter as VariableReference);
            }

            StringLiteral sl = new StringLiteral();
            if (castCall.DataType is SqlDataTypeReference)
            {
                var DataType = castCall.DataType as SqlDataTypeReference;
                if (DataType.SqlDataTypeOption == SqlDataTypeOption.NVarChar || DataType.SqlDataTypeOption == SqlDataTypeOption.VarChar)
                {
                    if (castCall.Parameter is IntegerLiteral
                        //||
                        )
                    {
                        sl.Value = (castCall.Parameter as Literal).Value;
                    }
                }
            }
            return sl;
        }
        Literal getResultFunctionCall(FunctionCall functionCall)
        {
            //Console.WriteLine("Нужно дописать выполнение скалярной функции!");
            if (string.Compare(functionCall.FunctionName.Value, "max", true) == 0)
            {
                foreach (var param in functionCall.Parameters)
                {
                    if (param is Literal)
                    {
                        messages.addMessage(Code.T0000047, param, functionCall.FunctionName.Value, (param as Literal).Value);
                    }
                    else
                    if (param is ColumnReferenceExpression)
                    {
                        var columnex = checkedColumnReference((param as ColumnReferenceExpression));
                    }
                }
            }
            else
            if (string.Compare(functionCall.FunctionName.Value, "object_id", true) == 0)
            {
                if (functionCall.Parameters.Count > 0)
                {
                    bool isValid = true;
                    foreach (var item in functionCall.Parameters)
                    {
                        if (!(item is StringLiteral))
                        {
                            isValid = false;
                            messages.addMessage(Code.T0000042, functionCall);
                            break;
                        }
                    }
                    if (isValid)
                    {
                        var par0 = (functionCall.Parameters[0] as StringLiteral).Value;
                        string baseIdentifier = getBaseIdentifier(par0);
                        if (string.IsNullOrEmpty(baseIdentifier))
                        {
                            messages.addMessage(Code.T0000043, functionCall, par0);
                            return null;
                        }
                        compareNull.Add(baseIdentifier, new ReferCount<FunctionCall, int>(functionCall, 0));
                    }
                }
            }


            return new StringLiteral();
        }
        T calculate<T, T1>(BinaryExpression val, Func<string, T1> parse, Func<T1, T1, T1> result)
             where T : Literal, new()
        {
            T res = new T();
            res.Value = result(parse((val.FirstExpression as Literal).Value), parse((val.SecondExpression as Literal).Value)).ToString();
            return res;
        }
        ScalarExpression convertExpression(ScalarExpression expression)
        {
            if (expression is Literal)
            {
                return expression;
            }
            if (expression is ConvertCall || expression is CastCall)
            {
                return getConvertOrCast(expression);
            }
            if (expression is VariableReference)
            {
                return GetLiteral(expression as VariableReference);
            }
            if (expression is FunctionCall)
            {
                return getResultFunctionCall(expression as FunctionCall);
            }
            if (expression is LeftFunctionCall)
            {
                return new StringLiteral();
            }
            if (expression is RightFunctionCall)
            {
                return new StringLiteral();
            }
            return new StringLiteral();
        }
        bool checkedBooleanComparisonExpression(BooleanComparisonExpression search)
        {
            bool IsCorrect = true;
            if (search.FirstExpression is ColumnReferenceExpression
                && search.SecondExpression is ColumnReferenceExpression)
            {
                MyColumn firstColumn = checkedColumnReference(search.FirstExpression as ColumnReferenceExpression);
                MyColumn secondColumn = checkedColumnReference(search.SecondExpression as ColumnReferenceExpression);

                if (firstColumn.IsValid && firstColumn.IsValid && firstColumn != null && secondColumn != null)
                {
                    checkColumnType(firstColumn.Column, secondColumn.Column);
                }

                if (((firstColumn.Alias == null && secondColumn.Alias != null)
                     || (firstColumn.Alias != null && secondColumn.Alias == null))
                    && string.Compare(firstColumn.Name, secondColumn.Name, IsIgnoreCase) == 0)
                {
                    messages.addMessage(Code.T0000031, search, secondColumn.Name);
                    IsCorrect = false;
                }

                if (firstColumn.Alias != null && secondColumn.Alias != null)
                {
                    if (string.Compare(firstColumn.Alias, secondColumn.Alias, IsIgnoreCase) == 0
                    && string.Compare(firstColumn.Name, secondColumn.Name, IsIgnoreCase) == 0
                    )
                    {
                        messages.addMessage(Code.T0000032, search, firstColumn.Alias);
                        IsCorrect = false;
                    }
                    else
                    if (firstColumn.Alias != null && secondColumn.Alias != null
                        && string.Compare(firstColumn.Alias, secondColumn.Alias, IsIgnoreCase) == 0)
                    {
                        messages.addMessage(Code.T0000025, search, firstColumn.Alias);
                        IsCorrect = false;
                    }
                }
            }
            else
            {
                if (search.FirstExpression is ColumnReferenceExpression)
                {
                    MyColumn firstColumn = checkedColumnReference(search.FirstExpression as ColumnReferenceExpression);

                    if (search.SecondExpression is VariableReference)
                    {

                    }
                    if (search.SecondExpression is Literal)
                    {
                        if (search.SecondExpression is NullLiteral)
                        {
                            if (search.ComparisonType == BooleanComparisonType.Equals)
                            {
                                messages.addMessage(Code.T0000020, search, firstColumn.FullName, search.ComparisonType.ToString());
                                IsCorrect = false;
                            }
                        }
                    }
                }
                else if (search.SecondExpression is ColumnReferenceExpression)
                {
                    MyColumn secondColumn = checkedColumnReference(search.SecondExpression as ColumnReferenceExpression);

                    if (search.FirstExpression is VariableReference)
                    {

                    }
                    if (search.FirstExpression is Literal)
                    {

                    }
                }
                else
                {

                }
            }
            return IsCorrect;
        }
        string getNameIdentifiers(MultiPartIdentifier multiPart)
        {
            if (multiPart is SchemaObjectName)
            {
                var obj = multiPart as SchemaObjectName;
                return getFullObject(obj.DatabaseIdentifier?.Value
                                   , obj.SchemaIdentifier?.Value
                                   , obj.BaseIdentifier.Value);
            }

            return string.Join(".", multiPart.Identifiers.Select(c => c.Value));
        }
        string getBaseIdentifier(string str)
        {
            string text = "";
            if (!str.Contains('.'))
            {
                text = getFullObject(null, null, str);
            }
            else
            {
                string[] res;
                if (str.Contains('[') && str.Contains(']'))
                {
                    res = str.Split(new[] { "].[", ".[", "]." }, StringSplitOptions.None).
                        Select(c => c.Replace("[", "").Replace("]", "")).ToArray();
                }
                else
                {
                    res = str.Split(new[] { "." }, StringSplitOptions.None);
                }
                if (res.Length > 0)
                {
                    text = getFullObject(
                          res.Length > 2 ? res[res.Length - 3] : null
                        , res.Length > 1 ? res[res.Length - 2] : null
                        , res.Length > 0 ? res[res.Length - 1] : null);
                }
            }
            return text;
        }
        string getFullObject(string Database, string Schema, string Base)
        {
            string res = "";
            res += string.IsNullOrEmpty(Database) ? (string.IsNullOrEmpty(database) ? "" : database + ".") : Database + ".";
            res += string.IsNullOrEmpty(Schema) ? "dbo." : Schema + ".";
            res += Base;
            return res;
        }
        bool checkedBooleanIsNullExpression(BooleanIsNullExpression booleanIsNullExpression)
        {
            if (booleanIsNullExpression.Expression is ColumnReferenceExpression)
            {
                var ex = booleanIsNullExpression.Expression as ColumnReferenceExpression;
                MyColumn myColumn = checkedColumnReference(ex);

                if (myColumn != null && !myColumn.IsNullable)
                {
                    //проверить что поле может принимать значение null, иначе сравнение не корректно
                    messages.addMessage(Code.T0000021, ex, getNameIdentifiers(ex.MultiPartIdentifier));
                    return false;
                }
                return true;
            }

            if (booleanIsNullExpression.Expression is FunctionCall)
            {
                getResultFunctionCall(booleanIsNullExpression.Expression as FunctionCall);
                return true;
            }
            return true;
        }
        public object CloneObject(object obj)
        {
            if (obj == null) return null;

            Type t1 = obj.GetType();
            object ret = Activator.CreateInstance(t1);

            var properties = t1.GetProperties().ToArray();
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i].SetMethod == null) continue;
                properties[i].SetValue(
                        ret,
                        properties[i].GetValue(obj)
                    );
            }
            return ret;
        }
    }
}

﻿--create table mytable(id int)

--create table #table(id int)

--declare @n nvarchar(1);
--Declare @tabl table(id int)

-- Declare @t1 bigint = 111111111111111111111111
-- Declare @t2 float = 5.0;

-- Declare @top int = 1;
-- Declare @t int = (select @top)
-- Declare @t3 int = (select top(@top) 1 from t where ttt = 1)
-- insert #table
-- select *,1, t.ta,@n,(select * from t) from tabl1 t

-- select @n
-- with s as(
-- 	select * from ttt
-- ),s as(
-- 	select * from ttt
-- )
-- select t.id,t1.namename
-- from --@tabl t 
-- 	t1 t 
-- 	--join t on t.id=t1.id
-- --join finance.tetra t1 on t.id = t1.parid and t1.name=t.part and t.i = t1.ii and @n = t1.ii11
-- where t.par is null
-- and t.name = t.reper;

-- t.id Equals t1.parid
-- t1.name Equals t.part
-- t.i Equals t1.ii

---Задачи:
--сказать что должен быть GO
alter table #ttt add FirmID int
update t set FirmID = f.ID
from #ttt t left join Firm f on t.Code2 = f.Code

---ошибки 41 44 не работают на примере:
if OBJECT_ID('[Import].[Mars_EngagementDepStage]') is not null drop table [Import].[Mars_EngagementDepStage];
go
CREATE TABLE [Import].[Mars_EngagementDepStage](
	[EngagementCode] [nvarchar](50) NOT NULL,
	[EmployeeCode] [nvarchar](50) NOT NULL,
	[DEP] [varchar](10) NOT NULL
) ON [PRIMARY]

--не говорить о том что при скалярной выборке использовать top если используется 1 агрегатная функция:
select max(IdEntityForm) from #entityform_temp where IdEntityForm is not null

---обработка транзакций
begin tran r
commit tran r
rollback tran r
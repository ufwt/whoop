﻿// ===-----------------------------------------------------------------------==//
//
//                 Whoop - a Verifier for Device Drivers
//
//  Copyright (c) 2013-2014 Pantazis Deligiannis (p.deligiannis@imperial.ac.uk)
//
//  This file is distributed under the Microsoft Public License.  See
//  LICENSE.TXT for details.
//
// ===----------------------------------------------------------------------===//

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

using Microsoft.Boogie;
using Microsoft.Basetypes;

using Whoop.Analysis;
using Whoop.Domain.Drivers;
using Whoop.Regions;

namespace Whoop.Instrumentation
{
  internal class RaceInstrumentation : IRaceInstrumentation
  {
    protected AnalysisContext AC;
    private EntryPoint EP;
    private ExecutionTimer Timer;

    public RaceInstrumentation(AnalysisContext ac, EntryPoint ep)
    {
      Contract.Requires(ac != null && ep != null);
      this.AC = ac;
      this.EP = ep;
    }

    public void Run()
    {
      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        this.Timer = new ExecutionTimer();
        this.Timer.Start();
      }

      this.AddAccessFuncs(AccessType.WRITE);
      this.AddAccessFuncs(AccessType.READ);

      foreach (var region in this.AC.InstrumentationRegions)
      {
        this.InstrumentImplementation(region);
        this.InstrumentProcedure(region);
      }

      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        this.Timer.Stop();
        Console.WriteLine(" |  |------ [RaceInstrumentation] {0}", this.Timer.Result());
      }
    }

    #region race checking verification variables and methods

    private void AddAccessFuncs(AccessType access)
    {
      foreach (var mr in SharedStateAnalyser.GetMemoryRegions(this.EP))
      {
        List<Variable> inParams = new List<Variable>();
        inParams.Add(new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ptr",
          this.AC.MemoryModelType)));

        Procedure proc = new Procedure(Token.NoToken, this.MakeAccessFuncName(access, mr.Name),
          new List<TypeVariable>(), inParams, new List<Variable>(),
          new List<Requires>(), new List<IdentifierExpr>(), new List<Ensures>());
        proc.AddAttribute("inline", new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(1)) });

        this.AC.TopLevelDeclarations.Add(proc);
        this.AC.ResContext.AddProcedure(proc);

        List<Variable> localVars = new List<Variable>();
        Implementation impl = new Implementation(Token.NoToken, this.MakeAccessFuncName(access, mr.Name),
          new List<TypeVariable>(), inParams, new List<Variable>(), localVars, new List<Block>());

        Block block = new Block(Token.NoToken, "_" + access.ToString(), new List<Cmd>(), new ReturnCmd(Token.NoToken));

        foreach (var ls in this.AC.MemoryLocksets)
        {
          if (!ls.TargetName.Equals(mr.Name))
            continue;

          foreach (var cls in this.AC.CurrentLocksets)
          {
            if (!cls.Lock.Name.Equals(ls.Lock.Name))
              continue;

//            Variable ptr = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ptr",
//              this.AC.MemoryModelType));
            Variable acs = this.AC.GetAccessCheckingVariables().Find(val =>
              val.Name.Contains(this.AC.GetAccessVariableName(this.EP, ls.TargetName)));
//            Variable offset = this.AC.GetAccessWatchdogConstants().Find(val =>
//              val.Name.Contains(this.AC.GetAccessWatchdogConstantName(ls.TargetName)));

            IdentifierExpr lsExpr = new IdentifierExpr(ls.Id.tok, ls.Id);
//            IdentifierExpr ptrExpr = new IdentifierExpr(ptr.tok, ptr);
            IdentifierExpr acsExpr = new IdentifierExpr(acs.tok, acs);
//            IdentifierExpr offsetExpr = new IdentifierExpr(offset.tok, offset);

//            block.Cmds.Add(new AssignCmd(Token.NoToken,
//              new List<AssignLhs>() {
//                new SimpleAssignLhs(Token.NoToken, lsExpr)
//              }, new List<Expr> { new NAryExpr(Token.NoToken,
//                new IfThenElse(Token.NoToken),
//                new List<Expr>(new Expr[] { Expr.Eq(ptrExpr, offsetExpr),
//                  Expr.And(new IdentifierExpr(cls.Id.tok, cls.Id), lsExpr), lsExpr
//                }))
//            }));

            block.Cmds.Add(new AssignCmd(Token.NoToken,
              new List<AssignLhs>() {
                new SimpleAssignLhs(Token.NoToken, lsExpr)
              }, new List<Expr> {
              Expr.And(new IdentifierExpr(cls.Id.tok, cls.Id), lsExpr)
            }));

            proc.Modifies.Add(lsExpr);

            if (access == AccessType.WRITE)
            {
//              block.Cmds.Add(new AssignCmd(Token.NoToken,
//                new List<AssignLhs>() {
//                  new SimpleAssignLhs(Token.NoToken, acsExpr)
//                }, new List<Expr> { new NAryExpr(Token.NoToken,
//                  new IfThenElse(Token.NoToken),
//                  new List<Expr>(new Expr[] { Expr.Eq(ptrExpr, offsetExpr),
//                    Expr.True, acsExpr
//                  }))
//              }));

              block.Cmds.Add(new AssignCmd(Token.NoToken,
                new List<AssignLhs>() {
                  new SimpleAssignLhs(Token.NoToken, acsExpr)
                }, new List<Expr> {
                Expr.True
              }));

              proc.Modifies.Add(acsExpr);
            }

            break;
          }
        }

        impl.Blocks.Add(block);
        impl.Proc = proc;
        impl.AddAttribute("inline", new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(1)) });

        this.AC.TopLevelDeclarations.Add(impl);
      }
    }

    #endregion

    #region race checking instrumentation

    private void InstrumentImplementation(InstrumentationRegion region)
    {
      foreach (var block in region.Blocks())
      {
        for (int idx = 0; idx < block.Cmds.Count; idx++)
        {
          Cmd c = block.Cmds[idx];
          if (!(c is AssignCmd)) continue;

          foreach (var lhs in (c as AssignCmd).Lhss.OfType<MapAssignLhs>())
          {
            if (!(lhs.DeepAssignedIdentifier.Name.Contains("$M.")) ||
              !(lhs.Map is SimpleAssignLhs) || lhs.Indexes.Count != 1)
              continue;

            var ind = lhs.Indexes[0];
            CallCmd call = new CallCmd(Token.NoToken,
              this.MakeAccessFuncName(AccessType.WRITE, lhs.DeepAssignedIdentifier.Name),
              new List<Expr> { ind }, new List<IdentifierExpr>());
            block.Cmds.Insert(idx + 1, call);
          }

          foreach (var rhs in (c as AssignCmd).Rhss.OfType<NAryExpr>())
          {
            if (!(rhs.Fun is MapSelect) || rhs.Args.Count != 2 ||
              !((rhs.Args[0] as IdentifierExpr).Name.Contains("$M.")))
              continue;

            CallCmd call = new CallCmd(Token.NoToken,
              this.MakeAccessFuncName(AccessType.READ, (rhs.Args[0] as IdentifierExpr).Name),
              new List<Expr> { rhs.Args[1] }, new List<IdentifierExpr>());
            block.Cmds.Insert(idx + 1, call);
          }
        }
      }
    }

    private void InstrumentProcedure(InstrumentationRegion region)
    {
      List<Variable> vars = SharedStateAnalyser.GetMemoryRegions(DeviceDriver.GetEntryPoint(this.EP.Name));

      foreach (var ls in this.AC.MemoryLocksets)
      {
        if (!vars.Any(val => val.Name.Equals(ls.TargetName)))
          continue;
        Variable acs = this.AC.GetAccessCheckingVariables().Find(val =>
          val.Name.Contains(this.AC.GetAccessVariableName(this.EP, ls.TargetName)));
        region.Procedure().Modifies.Add(new IdentifierExpr(acs.tok, acs));
      }
    }

    #endregion

    #region helper functions

    private string MakeAccessFuncName(AccessType access, string name)
    {
      return "_" + access.ToString() + "_LS_" + name + "_$" + this.EP.Name;
    }

    #endregion
  }
}

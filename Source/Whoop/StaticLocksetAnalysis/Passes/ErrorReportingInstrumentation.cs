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
using Whoop.Regions;

namespace Whoop.SLA
{
  internal class ErrorReportingInstrumentation : IErrorReportingInstrumentation
  {
    private AnalysisContext AC;

    public ErrorReportingInstrumentation(AnalysisContext ac)
    {
      Contract.Requires(ac != null);
      this.AC = ac;
    }

    public void Run()
    {
      this.InstrumentAsyncFuncs();
      this.CleanUp();
    }

    private void InstrumentAsyncFuncs()
    {
      foreach (var region in this.AC.LocksetAnalysisRegions)
      {
        this.InstrumentSourceLocationInfo(region);
        this.InstrumentRaceCheckingCaptureStates(region);

        if (!WhoopCommandLineOptions.Get().OnlyRaceChecking)
          this.InstrumentDeadlockCheckingCaptureStates(region);
      }
    }

    private void InstrumentSourceLocationInfo(LocksetAnalysisRegion region)
    {
      foreach (var b in region.Blocks())
      {
        for (int idx = 0; idx < b.Cmds.Count; idx++)
        {
          if (!(b.Cmds[idx] is CallCmd)) continue;
          CallCmd call = b.Cmds[idx] as CallCmd;

          if (call.callee.Contains("_UPDATE_CURRENT_LOCKSET"))
          {
            Contract.Requires(idx - 1 != 0 && b.Cmds[idx - 1] is AssumeCmd);
            call.Attributes = this.GetSourceLocationAttributes((b.Cmds[idx - 1] as AssumeCmd).Attributes);
          }
          else if (call.callee.Contains("_LOG_WRITE_LS_"))
          {
            Contract.Requires(idx - 1 != 0 && b.Cmds[idx - 1] is AssumeCmd);
            call.Attributes = this.GetSourceLocationAttributes((b.Cmds[idx - 1] as AssumeCmd).Attributes);
          }
          else if (call.callee.Contains("_LOG_READ_LS_"))
          {
            Contract.Requires(idx - 2 != 0 && b.Cmds[idx - 2] is AssumeCmd);
            call.Attributes = this.GetSourceLocationAttributes((b.Cmds[idx - 2] as AssumeCmd).Attributes);
          }
          else if (call.callee.Contains("_CHECK_WRITE_LS_"))
          {
            Contract.Requires(idx - 1 != 0 && b.Cmds[idx - 1] is AssumeCmd);
            call.Attributes = this.GetSourceLocationAttributes((b.Cmds[idx - 1] as AssumeCmd).Attributes);
          }
          else if (call.callee.Contains("_CHECK_READ_LS_"))
          {
            Contract.Requires(idx - 2 != 0 && b.Cmds[idx - 2] is AssumeCmd);
            call.Attributes = this.GetSourceLocationAttributes((b.Cmds[idx - 2] as AssumeCmd).Attributes);
          }
        }
      }
    }

    private void InstrumentRaceCheckingCaptureStates(LocksetAnalysisRegion region)
    {
      int logCounter = 0;
      int checkCounter = 0;

      AssumeCmd assumeLogHead = new AssumeCmd(Token.NoToken, Expr.True);
      assumeLogHead.Attributes = new QKeyValue(Token.NoToken, "captureState",
        new List<object>() { "logger_header_state" }, assumeLogHead.Attributes);
      region.Logger().Header().Cmds.Add(assumeLogHead);

      foreach (var checker in region.Checkers())
      {
        AssumeCmd assumeCheckHead = new AssumeCmd(Token.NoToken, Expr.True);
        assumeCheckHead.Attributes = new QKeyValue(Token.NoToken, "captureState",
          new List<object>() { "checker_header_state" }, assumeCheckHead.Attributes);
        checker.Header().Cmds.Add(assumeCheckHead);
      }

      foreach (var b in region.Blocks())
      {
        List<Cmd> newCmds = new List<Cmd>();

        foreach (var c in b.Cmds)
        {
          if (!(c is CallCmd))
          {
            newCmds.Add(c);
            continue;
          }

          CallCmd call = c as CallCmd;

          if (!(call.callee.Contains("_LOG_WRITE_LS_") ||
              call.callee.Contains("_LOG_READ_LS_") ||
              call.callee.Contains("_CHECK_WRITE_LS_") ||
              call.callee.Contains("_CHECK_READ_LS_")))
          {
            newCmds.Add(call);
            continue;
          }

          AssumeCmd assume = new AssumeCmd(Token.NoToken, Expr.True);

          assume.Attributes = new QKeyValue(Token.NoToken, "column",
            new List<object>() { new LiteralExpr(Token.NoToken,
                BigNum.FromInt(QKeyValue.FindIntAttribute(call.Attributes, "column", -1)))
            }, null);
          assume.Attributes = new QKeyValue(Token.NoToken, "line",
            new List<object>() { new LiteralExpr(Token.NoToken,
                BigNum.FromInt(QKeyValue.FindIntAttribute(call.Attributes, "line", -1)))
            }, assume.Attributes);

          if (call.callee.Contains("WRITE"))
            assume.Attributes = new QKeyValue(Token.NoToken, "access",
              new List<object>() { "write" }, assume.Attributes);
          else if (call.callee.Contains("READ"))
            assume.Attributes = new QKeyValue(Token.NoToken, "access",
              new List<object>() { "read" }, assume.Attributes);

          assume.Attributes = new QKeyValue(Token.NoToken, "entryPoint",
            new List<object>() { b.Label.Split(new char[] { '$' })[0] }, assume.Attributes);

          if (call.callee.Contains("_LOG_WRITE_LS_") ||
              call.callee.Contains("_LOG_READ_LS_"))
          {
            assume.Attributes = new QKeyValue(Token.NoToken, "captureState",
              new List<object>() { "log_state_" + logCounter++ }, assume.Attributes);
          }
          else
          {
            assume.Attributes = new QKeyValue(Token.NoToken, "captureState",
              new List<object>() { "check_state_" + checkCounter++ }, assume.Attributes);
          }

          assume.Attributes = new QKeyValue(Token.NoToken, "resource",
            new List<object>() { "$" + call.callee.Split(new char[] { '$' })[1] }, assume.Attributes);

          if (call.callee.Contains("_LOG_WRITE_LS_") ||
              call.callee.Contains("_LOG_READ_LS_"))
          {
            newCmds.Add(call);
            newCmds.Add(assume);
          }
          else
          {
            newCmds.Add(assume);
            newCmds.Add(call);
          }
        }

        b.Cmds = newCmds;
      }
    }

    private void InstrumentDeadlockCheckingCaptureStates(LocksetAnalysisRegion region)
    {
      int updateCounter = 0;

      foreach (var b in region.Blocks())
      {
        List<Cmd> newCmds = new List<Cmd>();

        foreach (var c in b.Cmds)
        {
          if (!(c is CallCmd))
          {
            newCmds.Add(c);
            continue;
          }

          CallCmd call = c as CallCmd;

          if (!(call.callee.Contains("_CHECK_ALL_LOCKS_HAVE_BEEN_RELEASED") ||
              call.callee.Contains("_UPDATE_CURRENT_LOCKSET")))
          {
            newCmds.Add(call);
            continue;
          }

          AssumeCmd assume = new AssumeCmd(Token.NoToken, Expr.True);

          if (call.callee.Contains("_UPDATE_CURRENT_LOCKSET"))
          {
            assume.Attributes = new QKeyValue(Token.NoToken, "column",
              new List<object>() { new LiteralExpr(Token.NoToken,
                  BigNum.FromInt(QKeyValue.FindIntAttribute(call.Attributes, "column", -1)))
              }, null);
            assume.Attributes = new QKeyValue(Token.NoToken, "line",
              new List<object>() { new LiteralExpr(Token.NoToken,
                  BigNum.FromInt(QKeyValue.FindIntAttribute(call.Attributes, "line", -1)))
              }, assume.Attributes);
          }

          assume.Attributes = new QKeyValue(Token.NoToken, "entryPoint",
            new List<object>() { b.Label.Split(new char[] { '$' })[0] }, assume.Attributes);

          if (call.callee.Contains("_UPDATE_CURRENT_LOCKSET"))
          {
            assume.Attributes = new QKeyValue(Token.NoToken, "captureState",
              new List<object>() { "update_cls_state_" + updateCounter++ }, assume.Attributes);

            newCmds.Add(call);
            newCmds.Add(assume);
          }
          else
          {
            assume.Attributes = new QKeyValue(Token.NoToken, "captureState",
              new List<object>() { "check_deadlock_state" }, assume.Attributes);

            newCmds.Add(assume);
            newCmds.Add(call);
          }
        }

        b.Cmds = newCmds;
      }
    }

    private QKeyValue GetSourceLocationAttributes(QKeyValue attributes)
    {
      QKeyValue line, col;
      QKeyValue curr = attributes;

      while (curr != null)
      {
        if (curr.Key.Equals("sourceloc")) break;
        curr = curr.Next;
      }
      Contract.Requires(curr.Key.Equals("sourceloc") && curr.Params.Count == 3);

      col = new QKeyValue(Token.NoToken, "column",
        new List<object>() { new LiteralExpr(Token.NoToken,
            BigNum.FromInt(int.Parse(string.Format("{0}", curr.Params[2]))))
        }, null);
      line = new QKeyValue(Token.NoToken, "line",
        new List<object>() { new LiteralExpr(Token.NoToken,
            BigNum.FromInt(int.Parse(string.Format("{0}", curr.Params[1]))))
        }, col);

      return line;
    }

    private void CleanUp()
    {
      foreach (var impl in this.AC.Program.TopLevelDeclarations.OfType<Implementation>())
      {
        foreach (Block b in impl.Blocks)
        {
          b.Cmds.RemoveAll(val => (val is AssumeCmd) && (val as AssumeCmd).Attributes != null &&
          (val as AssumeCmd).Attributes.Key.Equals("sourceloc"));
        }
      }
    }
  }
}
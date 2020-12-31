#region License notice

/*
  This file is part of the Ceres project at https://github.com/dje-dev/ceres.
  Copyright (C) 2020- by David Elliott and the Ceres Authors.

  Ceres is free software under the terms of the GNU General Public License v3.0.
  You should have received a copy of the GNU General Public License
  along with Ceres. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

#region Using directives

using System;
using System.Collections.Generic;

using Ceres.Base.Benchmarking;
using Ceres.Chess;
using Ceres.Chess.ExternalPrograms.UCI;
using Ceres.Chess.GameEngines;
using Ceres.Chess.LC0.Positions;
using Ceres.Chess.MoveGen;
using Ceres.Chess.NNEvaluators.Defs;
using Ceres.Chess.PositionEvalCaching;
using Ceres.Chess.Positions;
using Ceres.MCTS.Evaluators;
using Ceres.MCTS.Iteration;
using Ceres.MCTS.Managers.Limits;
using Ceres.MCTS.Params;

using Ceres.Features.UCI;

#endregion

namespace Ceres.Features.GameEngines
{
  /// <summary>
  /// Sublcass of GameEngine specialized for Ceres engine (in-process).
  /// </summary>
  public class GameEngineCeresInProcess : GameEngine
  {
    /// <summary>
    /// Optinal modifier action to be applied to ParamsSearchExecution before each search batch iteration.
    /// </summary>
    public readonly ParamsSearchExecutionModifier ParamsSearchExecutionModifier;

    /// <summary>
    /// Definition of neural network evaluator used for execution.
    /// </summary>
    public readonly NNEvaluatorDef EvaluatorDef;

    /// <summary>
    /// General search parameters used.
    /// </summary>
    public readonly ParamsSearch SearchParams;

    /// <summary>
    /// MCTS leaf selection parameters used.
    /// </summary>
    public readonly ParamsSelect ChildSelectParams;

    /// <summary>
    /// Manager used for approprtioning node or time limits at the game
    /// level to individual moves.
    /// </summary>
    public readonly IManagerGameLimit GameLimitManager;

    /// <summary>
    /// Manager used for previous search.
    /// </summary>
    public MCTSManager LastManager;

    #region Internal data

    /// <summary>
    /// Once created the NN evaluator pair is reused (until Dispose is called).
    /// </summary>
    NNEvaluatorSet evaluators = null;

    #endregion


    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="nnEvaluator"></param>
    /// <param name="searchParams"></param>
    /// <param name="childSelectParams"></param>
    /// <param name="gameLimitManager"></param>
    /// <param name="paramsSearchExecutionModifier"></param>
    public GameEngineCeresInProcess(string id, NNEvaluatorDef evaluatorDef, 
                                    ParamsSearch searchParams = null,
                                    ParamsSelect childSelectParams = null,
                                    IManagerGameLimit gameLimitManager = null,
                                    ParamsSearchExecutionModifier paramsSearchExecutionModifier = null) : base(id)
    {
      if (evaluatorDef == null) throw new ArgumentNullException(nameof(evaluatorDef));

      // Use default settings for search and select params if not specified.
      if (searchParams == null) searchParams = new ParamsSearch();
      if (childSelectParams == null) childSelectParams = new ParamsSelect();

      // Use default limit manager if not specified.
      if (gameLimitManager == null) gameLimitManager = new ManagerGameLimitCeres();

      ParamsSearchExecutionModifier = paramsSearchExecutionModifier;
      EvaluatorDef = evaluatorDef;
      SearchParams = searchParams;
      GameLimitManager = gameLimitManager;
      ChildSelectParams = childSelectParams;
    }


    bool isFirstMoveOfGame = true;

    /// <summary>
    /// Resets state to prepare for new game to be started.
    /// </summary>
    public override void ResetGame()
    {
      LastManager = null;
      isFirstMoveOfGame = true;
    }

        /// <summary>
    /// Executes any preparatory steps (that should not be counted in thinking time) before a search.
    /// </summary>
    protected override void DoSearchPrepare()
    {
    }

    /// <summary>
    /// Runs a search, calling DoSearch and adjusting the cumulative search time
    /// (convenience method with same functionality but returns the as the subclass
    /// GameEngineSearchResultCeres.
    /// </summary>
    /// <param name="curPositionAndMoves"></param>
    /// <param name="searchLimit"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    public GameEngineSearchResultCeres SearchCeres(PositionWithHistory curPositionAndMoves,
                                                   SearchLimit searchLimit,
                                                   List<GameMoveStat> gameMoveHistory = null,
                                                   ProgressCallback callback = null,
                                                   bool verbose = false)
    {
      return (GameEngineSearchResultCeres)Search(curPositionAndMoves, searchLimit, gameMoveHistory, callback, verbose);
    }

    /// <summary>
    /// Overriden virtual method which executes search.
    /// </summary>
    /// <param name="curPositionAndMoves"></param>
    /// <param name="searchLimit"></param>
    /// <param name="gameMoveHistory"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    protected override GameEngineSearchResult DoSearch(PositionWithHistory curPositionAndMoves, 
                                                       SearchLimit searchLimit,
                                                       List<GameMoveStat> gameMoveHistory, 
                                                       ProgressCallback callback,
                                                       bool verbose)
    {
      if (LastManager != null && curPositionAndMoves.InitialPosMG != LastManager.Context.StartPosAndPriorMoves.InitialPosMG)
        throw new Exception("ResetGame must be called if not continuing same line");

      (MCTSManager manager, MGMove bestMoveMG, TimingStats stats) searchResult;

      // Set up callback passthrough if provided
      MCTSManager.MCTSProgressCallback callbackMCTS = null;
      if (callback != null) callbackMCTS = callbackContext => callback((MCTSManager)callbackContext);

      // Possibly use the context of opponent to reuse position evaluations
      MCTSIterator shareContext = null;
      if (OpponentEngine is GameEngineCeresInProcess)
      {
        GameEngineCeresInProcess ceresOpponentEngine = OpponentEngine as GameEngineCeresInProcess;

        if (LastManager is not null
         && LastManager.Context.ParamsSearch.ReusePositionEvaluationsFromOtherTree
         && ceresOpponentEngine?.LastManager != null
         && LeafEvaluatorReuseOtherTree.ContextsCompatibleForReuse(LastManager.Context, ceresOpponentEngine.LastManager.Context))
        {
          shareContext = ceresOpponentEngine.LastManager.Context;

          // Clear any prior shared context from the shared context
          // to prevent unlimited backward chaining (keeping unneeded prior contexts alive)
          shareContext.ClearSharedContext();
        }
      }

      void InnerCallback(MCTSManager manager)
      {
        LastManager = manager;
        callbackMCTS?.Invoke(manager);
      }

    // Run the search
    searchResult = RunSearchPossiblyTreeReuse(shareContext, curPositionAndMoves, gameMoveHistory, 
                                                searchLimit, InnerCallback, verbose);

      int scoreCeresCP = (int)Math.Round(EncodedEvalLogistic.LogisticToCentipawn((float)searchResult.Item1.Root.Q), 0);

      MGMove bestMoveMG = searchResult.Item2;

      int N = (int)searchResult.Item1.Root.N;

      // Save (do not dispose) last manager in case we can reuse it next time
      LastManager = searchResult.manager;

      isFirstMoveOfGame = false;

      return new GameEngineSearchResultCeres(bestMoveMG.MoveStr(MGMoveNotationStyle.LC0Coordinate), 
                                             (float)searchResult.manager.Root.Q, scoreCeresCP, searchResult.manager.Root.MAvg, searchResult.manager.SearchLimit, default, 
                                             searchResult.manager.RootNWhenSearchStarted, N, (int)searchResult.manager.Context.AvgDepth, searchResult.manager);
    }


    /// <summary>
    /// Launches search, possibly as continuation from last search.
    /// </summary>
    /// <param name="reuseOtherContextForEvaluatedNodes"></param>
    /// <param name="curPositionAndMoves"></param>
    /// <param name="gameMoveHistory"></param>
    /// <param name="searchLimit"></param>
    /// <param name="VERBOSE"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    private (MCTSManager manager, MGMove bestMoveMG, TimingStats stats) 
      RunSearchPossiblyTreeReuse(MCTSIterator reuseOtherContextForEvaluatedNodes,
                                 PositionWithHistory curPositionAndMoves,
                                 List<GameMoveStat> gameMoveHistory, 
                                 SearchLimit searchLimit, 
                                 MCTSManager.MCTSProgressCallback callback,
                                 bool verbose)
    {
      DateTime startTime = DateTime.Now;

      (MCTSManager manager, MGMove bestMoveMG, TimingStats stats) result;

      PositionEvalCache positionCacheOpponent = null;

      if (LastManager == null)
      {
        if (evaluators == null) evaluators = new NNEvaluatorSet(EvaluatorDef);

        return MCTSLaunch.Search(evaluators, ChildSelectParams, SearchParams, GameLimitManager, 
                                 ParamsSearchExecutionModifier,
                                 reuseOtherContextForEvaluatedNodes,
                                 curPositionAndMoves, searchLimit, verbose, startTime,
                                 gameMoveHistory, callback, false, isFirstMoveOfGame);
      }

      if (LastManager.Context.StartPosAndPriorMoves.InitialPosMG != curPositionAndMoves.InitialPosMG)
        throw new Exception("Internal error: not same starting position");      
      
      List<MGMove> forwardMoves =  new List<MGMove>();
      List<MGMove> lastMoves = LastManager.Context.StartPosAndPriorMoves.Moves;
      for (int i = 0; i < curPositionAndMoves.Moves.Count; i++)
      {
        if (i < lastMoves.Count)
        {
          if (lastMoves[i] != curPositionAndMoves.Moves[i])
            throw new Exception("Internal error: move sequence is not a prefix");
        }
        else
          forwardMoves.Add(curPositionAndMoves.Moves[i]);
      }

      // Determine the minimum fraction of tree that would need to be useful
      // before we would possibly reuse part of tree from prior search.
      // Below a certain level the tree reuse would increase memory consumption
      // (because unused nodes remain in node store) but not appreciably improve search speed.
      //TODO: tweak this parameter, possibly make it larger if small hardwre config is in use
      float THRESHOLD_FRACTION_NODES_REUSABLE = 0.05f;
      return MCTSLaunch.SearchContinue(LastManager, reuseOtherContextForEvaluatedNodes,
                                       forwardMoves, curPositionAndMoves, 
                                       gameMoveHistory, searchLimit, verbose, startTime,
                                       callback, THRESHOLD_FRACTION_NODES_REUSABLE,
                                       isFirstMoveOfGame);
    }

    public override void DumpMoveHistory(List<GameMoveStat> gameMoveHistory, SideType? side)
    {
      // TODO: fill in increemental time below (last argument)
      ManagerGameLimitInputs timeManagerInputs = new (LastManager.Context.StartPosAndPriorMoves.FinalPosition,
                                                LastManager.Context.ParamsSearch,
                                                gameMoveHistory, SearchLimitType.SecondsPerMove,
                                                LastManager.Root.N, (float)LastManager.Root.Q,
                                                LastManager.SearchLimit.Value, 0, 0, 0);
      timeManagerInputs.Dump(side);
    }


    /// <summary>
    /// Returns UCI information string 
    /// (such as would appear in a chess GUI describing search progress) 
    /// based on last state of search.
    /// </summary>
    public override UCISearchInfo UCIInfo
    {
      get
      {
        if (LastManager != null)
          return new UCISearchInfo(UCIManager.UCIInfoString(LastManager));
        else
          return null;
      }
    }


    /// <summary>
    /// Diposes underlying search engine.
    /// </summary>
    public override void Dispose()
    {
      evaluators?.Dispose();
      evaluators = null;

      LastManager?.Dispose();
      LastManager = null;
    }

  }
}

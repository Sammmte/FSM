﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Paps.FSM
{
    public class FSM<TState, TTrigger> : IFSM<TState, TTrigger>, IFSMWithGuardConditions<TState, TTrigger>
    {
        public int StateCount => _states.Count;
        public int TransitionCount => _transitions.Count;
        public bool IsStarted { get; private set; }
        public TState InitialState { get; private set; }

        public event StateChange<TState, TTrigger> OnBeforeStateChanges;
        public event StateChange<TState, TTrigger> OnStateChanged;

        private Dictionary<TState, IState> _states;
        private HashSet<ITransition<TState, TTrigger>> _transitions;
        private Dictionary<ITransition<TState, TTrigger>, List<IGuardCondition<TState, TTrigger>>> guardConditions;

        private Queue<TransitionRequest> _transitionRequestQueue;

        private TState _currentState;
        private IState _currentStateObject;
        private bool _isTransitioning;

        private IEqualityComparer<TState> _stateComparer;
        private IEqualityComparer<TTrigger> _triggerComparer;
        
        private FSMTransitionEqualityComparer _transitionEqualityComparer;
        
        public FSM(IEqualityComparer<TState> stateComparer, IEqualityComparer<TTrigger> triggerComparer)
        {
            if (stateComparer == null) throw new ArgumentNullException(nameof(stateComparer));
            if (triggerComparer == null) throw new ArgumentNullException(nameof(triggerComparer));

            _stateComparer = stateComparer;
            _triggerComparer = triggerComparer;

            _transitionEqualityComparer = new FSMTransitionEqualityComparer(stateComparer, triggerComparer);

            _states = new Dictionary<TState, IState>(_stateComparer);
            _transitions = new HashSet<ITransition<TState, TTrigger>>();
            guardConditions = new Dictionary<ITransition<TState, TTrigger>, List<IGuardCondition<TState, TTrigger>>>(_transitionEqualityComparer);
            _transitionRequestQueue = new Queue<TransitionRequest>();
        }
        
        public FSM(Func<TState, TState, bool> stateComparer, Func<TTrigger, TTrigger, bool> triggerComparer) : this(new Comparer<TState>(stateComparer), new Comparer<TTrigger>(triggerComparer))
        {

        }

        public FSM() : this(EqualityComparer<TState>.Default, EqualityComparer<TTrigger>.Default)
        {
            
        }

        public void SetStateComparer(IEqualityComparer<TState> stateComparer)
        {
            if (stateComparer == null) throw new ArgumentNullException(nameof(stateComparer));

            _stateComparer = stateComparer;
        }

        public void SetTriggerComparer(IEqualityComparer<TTrigger> triggerComparer)
        {
            if (triggerComparer == null) throw new ArgumentNullException(nameof(triggerComparer));

            _triggerComparer = triggerComparer;
        }

        private static bool DefaultComparer<T>(T first, T second)
        {
            return first.Equals(second);
        }

        public void Start()
        {
            ValidateCanStart();

            _currentState = InitialState;
            _currentStateObject = GetStateById(_currentState);

            IsStarted = true;

            EnterCurrentState();
        }

        private void ValidateCanStart()
        {
            ValidateIsNotStarted();
            ValidateInitialState(InitialState);
        }
        
        private void EnterCurrentState()
        {
            _currentStateObject.Enter();
        }

        public void Update()
        {
            UpdateCurrentState();
        }

        private void UpdateCurrentState()
        {
            ValidateIsStarted();

            _currentStateObject.Update();
        }

        public void Stop()
        {
            bool wasStarted = IsStarted;

            IsStarted = false;

            if (wasStarted)
            {
                ExitCurrentState();
            }
        }

        private void ExitCurrentState()
        {
            _currentStateObject.Exit();
        }

        public void SetInitialState(TState stateId)
        {
            ValidateCanSetInitialState(stateId);

            InternalSetInitialState(stateId);
        }

        private void ValidateCanSetInitialState(TState initialStateId)
        {
            ValidateIsNotStarted();
            ValidateInitialState(initialStateId);
        }

        private void ValidateInitialState(TState initialStateId)
        {
            if(ContainsState(initialStateId) == false)
            {
                throw new InvalidInitialStateException();
            }
        }

        private void ValidateIsStarted()
        {
            if (IsStarted == false)
            {
                throw new FSMNotStartedException();
            }
        }

        private void ValidateIsNotStarted()
        {
            if (IsStarted)
            {
                throw new FSMStartedException();
            }
        }

        public TState[] GetStates()
        {
            return _states.Keys.ToArray();
        }

        public ITransition<TState, TTrigger>[] GetTransitions()
        {
            return _transitions.ToArray();
        }

        private void InternalSetInitialState(TState stateId)
        {
            InitialState = stateId;
        }

        public void AddState(TState stateId, IState state)
        {
            ValidateCanAddState(stateId, state);

            InternalAddState(stateId, state);
        }

        private void ValidateCanAddState(TState stateId, IState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            else if (_states.ContainsKey(stateId))
            {
                throw new StateIdAlreadyAddedException("State id " + stateId + " was already added to state machine");
            }
        }

        private void InternalAddState(TState stateId, IState state)
        {
            _states.Add(stateId, state);
        }

        public void RemoveState(TState stateId)
        {
            if(_states.Remove(stateId))
            {
                RemoveTransitionsRelatedTo(stateId);
            }
        }

        private void RemoveTransitionsRelatedTo(TState stateId)
        {
            ITransition<TState, TTrigger>[] transitions = GetTransitionsRelatedTo(stateId);

            for(int i = 0; i < transitions.Length; i++)
            {
                var transition = transitions[i];

                InternalRemoveTransition(transition.StateFrom, transition.Trigger, transition.StateTo);
            }
        }

        private ITransition<TState, TTrigger>[] GetTransitionsRelatedTo(TState stateId)
        {
            return _transitions.Where(
                (transition) => _stateComparer.Equals(transition.StateFrom, stateId) || _stateComparer.Equals(transition.StateTo, stateId)
                ).ToArray();
        }

        public IState GetStateById(TState stateId)
        {
            if(_states.ContainsKey(stateId))
            {
                return _states[stateId];
            }

            throw new StateIdNotAddedException(stateId.ToString());
        }

        public void AddTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            ValidateHasStateWithId(stateFrom);
            ValidateHasStateWithId(stateTo);

            InternalAddTransition(stateFrom, trigger, stateTo);
        }

        private void InternalAddTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            _transitions.Add(new Transition<TState, TTrigger>(stateFrom, trigger, stateTo));
        }

        public void RemoveTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            ValidateHasStateWithId(stateFrom);
            ValidateHasStateWithId(stateTo);

            InternalRemoveTransition(stateFrom, trigger, stateTo);
        }

        private void InternalRemoveTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            var transition = GetTransition(stateFrom, trigger, stateTo);

            if (transition != null && _transitions.Remove(transition))
            {
                RemoveAllGuardConditionsRelatedTo(transition);
            }
        }

        private void RemoveAllGuardConditionsRelatedTo(ITransition<TState, TTrigger> transition)
        {
            guardConditions.Remove(transition);
        }

        private void ValidateHasStateWithId(TState stateId)
        {
            if(ContainsState(stateId) == false)
            {
                throw new StateIdNotAddedException(stateId.ToString());
            }
        }

        public bool IsInState(TState stateId)
        {
            return IsStarted && _stateComparer.Equals(_currentState, stateId);
        }

        public bool ContainsState(TState stateId)
        {
            return _states.ContainsKey(stateId);
        }

        public bool ContainsTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            var transition = GetTransition(stateFrom, trigger, stateTo);

            if(transition != null)
            {
                return true;
            }

            return false;
        }

        public void Trigger(TTrigger trigger)
        {
            ValidateIsStarted();

            _transitionRequestQueue.Enqueue(new TransitionRequest() { trigger = trigger });

            if (_isTransitioning == false)
            {
                _isTransitioning = true;
                TriggerQueued();
            }
        }
        
        private void TriggerQueued()
        {
            while(_transitionRequestQueue.Count > 0)
            {
                TransitionRequest transition = _transitionRequestQueue.Dequeue();

                TState stateTo = default;

                try
                {
                    if(TryGetStateTo(transition.trigger, out stateTo))
                    {
                        Transition(stateTo);
                    }
                }
                catch(MultipleValidTransitionsFromSameStateException e)
                {
                    _transitionRequestQueue.Clear();
                    _isTransitioning = false;
                    throw;
                }
                catch
                {
                    throw;
                }
            }

            _isTransitioning = false;
        }

        private bool TryGetStateTo(TTrigger trigger, out TState stateTo)
        {
            stateTo = default;

            bool modifiedFlag = false;
            bool multipleValidGuardsFlag = false;

            foreach (ITransition<TState, TTrigger> transition in _transitions)
            {
                if (_stateComparer.Equals(transition.StateFrom, _currentState) 
                    && _triggerComparer.Equals(transition.Trigger, trigger)
                    && IsValidTransition(transition))
                {
                    if(multipleValidGuardsFlag)
                    {
                        throw new MultipleValidTransitionsFromSameStateException(_currentState.ToString(), trigger.ToString());
                    }
                    
                    stateTo = transition.StateTo;

                    modifiedFlag = true;
                    multipleValidGuardsFlag = true;
                }
            }

            return modifiedFlag;
        } 

        private void Transition(TState stateTo)
        {
            CallOnBeforeStateChangesEvent();

            ExitCurrentState();
            
            _currentState = stateTo;
            _currentStateObject = GetStateById(_currentState);

            CallOnStateChangedEvent();

            EnterCurrentState();
        }

        private ITransition<TState, TTrigger> GetTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            foreach (ITransition<TState, TTrigger> transition in _transitions)
            {
                if (_transitionEqualityComparer.Equals(transition, stateFrom, trigger, stateTo))
                {
                    return transition;
                }
            }

            return null;
        }

        private bool IsValidTransition(ITransition<TState, TTrigger> transition)
        {
            return AllGuardConditionsTrueOrHasNone(transition);
        }

        private bool AllGuardConditionsTrueOrHasNone(ITransition<TState, TTrigger> transition)
        {
            if(guardConditions.ContainsKey(transition))
            {
                var guardConditionsOfTransition = guardConditions[transition];

                foreach (var guardCondition in guardConditionsOfTransition)
                {
                    if (guardCondition.IsValid(transition.StateFrom, transition.Trigger, transition.StateTo) == false)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void CallOnStateChangedEvent()
        {
            OnStateChanged?.Invoke(this);
        }

        private void CallOnBeforeStateChangesEvent()
        {
            OnBeforeStateChanges?.Invoke(this);
        }

        public void AddGuardConditionTo(TState stateFrom, TTrigger trigger, TState stateTo, IGuardCondition<TState, TTrigger> guardCondition)
        {
            ValidateHasTransition(stateFrom, trigger, stateTo);
            ValidateGuardConditionIsNotNull(guardCondition);

            var transition = GetTransition(stateFrom, trigger, stateTo);

            if(guardConditions.ContainsKey(transition) == false)
            {
                guardConditions.Add(transition, new List<IGuardCondition<TState, TTrigger>>());
            }

            guardConditions[transition].Add(guardCondition);
        }

        public void RemoveGuardConditionFrom(TState stateFrom, TTrigger trigger, TState stateTo, IGuardCondition<TState, TTrigger> guardCondition)
        {
            ValidateHasTransition(stateFrom, trigger, stateTo);
            ValidateGuardConditionIsNotNull(guardCondition);

            var transition = GetTransition(stateFrom, trigger, stateTo);

            if(guardConditions.ContainsKey(transition))
            {
                guardConditions[transition].Remove(guardCondition);

                if(guardConditions[transition].Count == 0)
                {
                    guardConditions.Remove(transition);
                }
            }
        }

        public bool ContainsGuardConditionOn(TState stateFrom, TTrigger trigger, TState stateTo, IGuardCondition<TState, TTrigger> guardCondition)
        {
            var transition = GetTransition(stateFrom, trigger, stateTo);
            
            if(transition != null && guardConditions.ContainsKey(transition))
            {
                return guardConditions[transition].Contains(guardCondition);
            }

            return false;
        }

        public KeyValuePair<ITransition<TState, TTrigger>, IGuardCondition<TState, TTrigger>[]>[] GetGuardConditions()
        {
            var keyValues = new KeyValuePair<ITransition<TState, TTrigger>, IGuardCondition<TState, TTrigger>[]>[guardConditions.Count];

            int index = 0;

            foreach(var keyValue in guardConditions)
            {
                keyValues[index] = new KeyValuePair<ITransition<TState, TTrigger>, IGuardCondition<TState, TTrigger>[]>(keyValue.Key, keyValue.Value.ToArray());
                index++;
            }

            return keyValues;
        }

        private void ValidateHasTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            if(ContainsTransition(stateFrom, trigger, stateTo) == false)
            {
                throw new TransitionNotAddedException(stateFrom.ToString(), trigger.ToString(), stateTo.ToString());
            }
        }

        private void ValidateGuardConditionIsNotNull(object guardCondition)
        {
            if(guardCondition == null)
            {
                throw new ArgumentNullException("Guard condition was null");
            }
        }

        public bool SendEvent(IEvent messageEvent)
        {
            ValidateIsStarted();

            return _currentStateObject.HandleEvent(messageEvent);
        }

        private struct TransitionRequest
        {
            public TTrigger trigger;
        }

        private class FSMTransitionEqualityComparer : IEqualityComparer<ITransition<TState, TTrigger>>
        {
            private IEqualityComparer<TState> _stateComparer;
            private IEqualityComparer<TTrigger> _triggerComparer;

            public FSMTransitionEqualityComparer(IEqualityComparer<TState> stateComparer, IEqualityComparer<TTrigger> triggerComparer)
            {
                _stateComparer = stateComparer;
                _triggerComparer = triggerComparer;
            }

            public bool Equals(ITransition<TState, TTrigger> x, ITransition<TState, TTrigger> y)
            {
                return _stateComparer.Equals(x.StateFrom, y.StateFrom) && _triggerComparer.Equals(x.Trigger, y.Trigger) && _stateComparer.Equals(x.StateTo, y.StateTo);
            }

            public int GetHashCode(ITransition<TState, TTrigger> obj)
            {
                return (obj.StateFrom, obj.Trigger, obj.StateTo).GetHashCode();
            }

            public bool Equals(ITransition<TState, TTrigger> transition, TState stateFrom, TTrigger trigger, TState stateTo)
            {
                return _stateComparer.Equals(transition.StateFrom, stateFrom) && _triggerComparer.Equals(transition.Trigger, trigger) && _stateComparer.Equals(transition.StateTo, stateTo);
            }
        }

        private class Comparer<T> : IEqualityComparer<T>
        {
            Func<T, T, bool> _comparer;

            public Comparer(Func<T, T, bool> comparer)
            {
                _comparer = comparer ?? ((param1, param2) => param1.Equals(param2));
            }

            public bool Equals(T x, T y)
            {
                return _comparer(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}

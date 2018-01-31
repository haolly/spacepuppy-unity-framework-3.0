﻿#pragma warning disable 0414 // variable declared but not used.
#pragma warning disable 0649 // variable declared but not used.

using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

using com.spacepuppy;
using com.spacepuppy.Anim.Legacy;
using com.spacepuppy.Events;
using com.spacepuppy.Utils;

namespace com.spacepuppy.Anim.Events
{
    public class i_PlayAnimation : AutoTriggerable, IObservableTrigger
    {

        private const string TRG_ONANIMCOMPLETE = "OnAnimComplete";

        public enum PlayByMode
        {
            PlayAnim,
            PlayAnimByID,
            PlayAnimFromResource
        }

        #region Fields

        [SerializeField]
        [TriggerableTargetObject.Config(typeof(UnityEngine.Object))]
        private TriggerableTargetObject _targetAnimator;

        [SerializeField]
        private PlayByMode _mode;
        [SerializeField]
        private string _id;
        [SerializeField]
        private UnityEngine.Object _clip;

        [SerializeField]
        private AnimSettings _settings = AnimSettings.Default;

        [SerializeField]
        private QueueMode _queueMode = QueueMode.PlayNow;
        [SerializeField]
        private PlayMode _playMode = PlayMode.StopSameLayer;
        [SerializeField]
        private float _crossFadeDur = 0f;

        [SerializeField()]
        private SPEvent _onAnimComplete = new SPEvent(TRG_ONANIMCOMPLETE);
        [SerializeField()]
        [Tooltip("If an animation doesn't play, should we signal complete. This is useful if the animation is supposed to be chaining to another i_ that MUST run.")]
        private bool _triggerCompleteIfNoAnim = true;
        [SerializeField()]
        [Tooltip("If this is called as a BlockingTriggerableMechanims, should it actually block?")]
        private bool _useAsBlockingYieldInstruction = true;
        [SerializeField()]
        [Tooltip("When this mechanism is called as a BlockingTriggerableMechanims, it will block the caller until complete. Set this true to allow the next step in the daisy chain to also block.")]
        private bool _daisyChainBlockingYieldInstruction = true;

        #endregion

        #region Methods

        private object PlayClip(object controller, UnityEngine.Object clip)
        {
            if (clip is AnimationClip)
            {
                if (controller is SPLegacyAnimController)
                {
                    var anim = (controller as SPLegacyAnimController).CreateAuxiliarySPAnim(clip as AnimationClip);
                    _settings.Apply(anim);
                    if (_crossFadeDur > 0f)
                        anim.CrossFade(_crossFadeDur, _queueMode, _playMode);
                    else
                        anim.Play(_queueMode, _playMode);
                    return anim;
                }
                else if (controller is Animation)
                {
                    var animController = controller as Animation;
                    var id = "aux*" + clip.GetInstanceID();
                    var a = animController[id];
                    if(a == null || a.clip != clip)
                    {
                        animController.AddClip(clip as AnimationClip, id);
                    }

                    AnimationState anim;
                    if (_crossFadeDur > 0f)
                        anim = animController.CrossFadeQueued(id, _crossFadeDur, _queueMode, _playMode);
                    else
                        anim = animController.PlayQueued(id, _queueMode, _playMode);
                    _settings.Apply(anim);
                    return anim;
                }
            }
            else if (clip is IScriptableAnimationClip)
            {
                if (controller is SPLegacyAnimController)
                {
                    return (controller as SPLegacyAnimController).Play(clip as IScriptableAnimationClip);
                }
            }

            return null;
        }

        private object TryPlay(object controller)
        {
            switch (_mode)
            {
                case PlayByMode.PlayAnim:
                    return PlayClip(controller, _clip);
                case PlayByMode.PlayAnimByID:
                    {
                        if (controller is ISPAnimationSource)
                        {
                            var anim = (controller as ISPAnimationSource).GetAnim(_id);
                            if (anim != null)
                            {
                                if (_crossFadeDur > 0f)
                                    anim.CrossFade(_crossFadeDur, _queueMode, _playMode);
                                else
                                    anim.Play(_queueMode, _playMode);
                            }
                            return anim;
                        }
                        else if(controller is ISPAnimator)
                        {
                            (controller as ISPAnimator).Play(_id, _queueMode, _playMode);
                            return SPAnim.Null;
                        }
                        else if(controller is Animation)
                        {
                            var clip = (controller as Animation)[_id];
                            if(clip != null)
                            {
                                AnimationState anim;
                                if (_crossFadeDur > 0f)
                                    anim = (controller as Animation).CrossFadeQueued(_id, _crossFadeDur, _queueMode, _playMode);
                                else
                                    anim = (controller as Animation).PlayQueued(_id, _queueMode, _playMode);
                                _settings.Apply(anim);
                                return anim;
                            }
                        }

                        return null;
                    }
                case PlayByMode.PlayAnimFromResource:
                    return this.PlayClip(controller, Resources.Load<UnityEngine.Object>(_id));
                default:
                    return null;
            }
        }

        private object ResolveTargetAnimator(object arg)
        {
            var obj = _targetAnimator.GetTarget<UnityEngine.Object>(arg);

            ISPAnimationSource src = null;
            ISPAnimator spanim = null;
            Animation anim = null;

            if (ObjUtil.GetAsFromSource<ISPAnimationSource>(obj, out src))
                return src;
            if (ObjUtil.GetAsFromSource<ISPAnimator>(obj, out spanim))
                return spanim;
            if (ObjUtil.GetAsFromSource<Animation>(obj, out anim))
                return anim;

            if(_targetAnimator.ImplicityReducesEntireEntity)
            {
                var go = GameObjectUtil.FindRoot(GameObjectUtil.GetGameObjectFromSource(obj));
                if (go == null) return null;

                SPLegacyAnimController spcont;
                if (go.FindComponent<SPLegacyAnimController>(out spcont))
                    return spcont;
                
                if (go.FindComponent<Animation>(out anim))
                    return anim;
            }
            
            return null;
        }



        public override bool Trigger(object sender, object arg)
        {
            if (!this.CanTrigger) return false;

            var targ = this.ResolveTargetAnimator(arg);
            if (targ == null) return false;

            var anim = this.TryPlay(targ);
            if (anim == null)
            {
                if (_triggerCompleteIfNoAnim) this.Invoke(() => { _onAnimComplete.ActivateTrigger(this, arg); }, 0.01f);
                return false;
            }

            if (_onAnimComplete.Count > 0)
            {
                if (anim is ISPAnim)
                {
                    (anim as ISPAnim).Schedule((s) =>
                    {
                        _onAnimComplete.ActivateTrigger(this, arg);
                    });
                }
                else if (anim is AnimationState)
                {
                    GameLoop.Hook.StartCoroutine((anim as AnimationState).ScheduleLegacy(() =>
                    {
                        _onAnimComplete.ActivateTrigger(this, arg);
                    }));
                }
            }

            return false;
        }

        #endregion

        #region IObservableTrigger Interface

        BaseSPEvent[] IObservableTrigger.GetEvents()
        {
            return new BaseSPEvent[] { _onAnimComplete };
        }

        #endregion

        #region Static Interface

        public static bool IsAcceptibleAnimator(object obj)
        {
            return obj is ISPAnimationSource || obj is ISPAnimator || obj is Animation;
        }

        #endregion

    }
}

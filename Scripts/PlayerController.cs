using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TarodevController
{
    /// <summary>
    /// Hey!
    /// Tarodev here. I built this controller as there was a severe lack of quality & free 2D controllers out there.
    /// Right now it only contains movement and jumping, but it should be pretty easy to expand... I may even do it myself
    /// if there's enough interest. You can play and compete for best times here: https://tarodev.itch.io/
    /// If you hve any questions or would like to brag about your score, come to discord: https://discord.gg/GqeHHnhHpz
    /// </summary>
    public class PlayerController : MonoBehaviour, IPlayerController
    {
        // Public for external hooks
        public Vector3 Velocity { get; private set; }
        public FrameInput Input { get; private set; }
        public bool JumpingThisFrame { get; private set; }
        public bool LandingThisFrame { get; private set; }
        public Vector3 RawMovement { get; private set; }
        public bool Grounded => _colDown;

        private Vector3 _lastPosition;
        private float _currentHorizontalSpeed, _currentVerticalSpeed;

        // This is horrible, but for some reason colliders are not fully established when update starts...
        // 根据作者所说，在Player的Update开始运行时，其他物体的collider可能还没完全准备好，
        // 故而在Player的Awake执行完后延迟0.5s再开始Update
        private bool _active;
        void Awake() => Invoke(nameof(Activate), 0.5f);
        void Activate() => _active = true;

        private void Update()
        {
            if (!_active) return;

            // 单纯根据Player两帧间的位移得到速度
            Velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;

            GatherInput(); // 获取输入
            RunCollisionChecks(); // 碰撞检测

            CalculateWalk(); // 水平移动（计算水平速度）

            // 下坠/重力（计算垂直速度）
            CalculateJumpApex();
            CalculateGravity();

            CalculateJump(); // 跳跃（设置垂直速度）

            MoveCharacter(); // 移动角色
        }


        #region Gather Input

        private void GatherInput()
        {
            Input = new FrameInput
            {
                JumpDown = UnityEngine.Input.GetButtonDown("Jump"),
                JumpUp = UnityEngine.Input.GetButtonUp("Jump"),
                X = UnityEngine.Input.GetAxisRaw("Horizontal")
            };
            if (Input.JumpDown)
            {
                _lastJumpPressed = Time.time;
            }
        }

        #endregion

        #region Collisions

        [Header("COLLISION")]

        [SerializeField, Tooltip("角色的四边界大小")]
        private Bounds _characterBounds;

        [SerializeField, Tooltip("射线检测的Layer")]
        private LayerMask _groundLayer;

        [SerializeField, Tooltip("每个方向发射的射线数量")]
        private int _detectorCount = 3;

        [SerializeField, Tooltip("射线检测距离")]
        private float _detectionRayLength = 0.1f;

        [SerializeField, Tooltip("发射线区域与边缘的缓冲区大小")]
        [Range(0.1f, 0.3f)]
        private float _rayBuffer = 0.1f; //增大数值可以尽量避免侧向的射线碰撞到地板

        private RayRange _raysUp, _raysRight, _raysDown, _raysLeft; // 四个方向的RayRange参数
        private bool _colUp, _colRight, _colDown, _colLeft; // 分别表示四个方向是否发生碰撞

        private float _timeLeftGrounded; // 记录离开地面时的时间

        /// <summary>
        /// 通过在四个方向上发射若干射线，进行四个方向上的碰撞检测
        /// 影响参数：_colUp, _colRight, _colDown, _colLeft
        /// </summary>
        private void RunCollisionChecks()
        {
            // 初始化四个方向上的RagRange参数
            CalculateRayRanged();

            // Ground
            LandingThisFrame = false;
            var groundedCheck = RunDetection(_raysDown);
            if (_colDown && !groundedCheck) _timeLeftGrounded = Time.time; //第一帧离开地面，记录离开地面的时间
            else if (!_colDown && groundedCheck)
            {
                _coyoteUsable = true; // 第一帧落到地上
                LandingThisFrame = true;
            }

            _colDown = groundedCheck;

            _colUp = RunDetection(_raysUp);
            _colLeft = RunDetection(_raysLeft);
            _colRight = RunDetection(_raysRight);

            bool RunDetection(RayRange range)
            {
                return EvaluateRayPositions(range).Any(point => Physics2D.Raycast(point, range.Dir, _detectionRayLength, _groundLayer));
            }
        }

        /// <summary>
        /// 计算四个方向上发射射线的范围；
        /// 即确定射线的起点线，终点线，方向。
        /// 影响参数：_raysUp, _raysRight, _raysDown, _raysLeft
        /// </summary>
        private void CalculateRayRanged()
        {
            // 根据当前位置和参数中设定的检测盒大小，生成一个检测盒，以检测盒四个边界为准，修改RayRange
            var b = new Bounds(transform.position, _characterBounds.size);

            _raysDown = new RayRange(b.min.x + _rayBuffer, b.min.y, b.max.x - _rayBuffer, b.min.y, Vector2.down);
            _raysUp = new RayRange(b.min.x + _rayBuffer, b.max.y, b.max.x - _rayBuffer, b.max.y, Vector2.up);
            _raysLeft = new RayRange(b.min.x, b.min.y + _rayBuffer, b.min.x, b.max.y - _rayBuffer, Vector2.left);
            _raysRight = new RayRange(b.max.x, b.min.y + _rayBuffer, b.max.x, b.max.y - _rayBuffer, Vector2.right);
        }


        private IEnumerable<Vector2> EvaluateRayPositions(RayRange range)
        {
            for (var i = 0; i < _detectorCount; i++)
            {
                var t = (float)i / (_detectorCount - 1);
                yield return Vector2.Lerp(range.Start, range.End, t);
            }
        }

        private void OnDrawGizmos()
        {
            // 绘制Bounds
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + _characterBounds.center, _characterBounds.size);

            // 绘制射线
            if (!Application.isPlaying)
            {
                CalculateRayRanged();
                Gizmos.color = Color.blue;
                foreach (var range in new List<RayRange> { _raysUp, _raysRight, _raysDown, _raysLeft })
                {
                    foreach (var point in EvaluateRayPositions(range))
                    {
                        Gizmos.DrawRay(point, range.Dir * _detectionRayLength);
                    }
                }
            }

            if (!Application.isPlaying) return;

            // 绘制下一帧理论位置
            Gizmos.color = Color.red;
            var move = new Vector3(_currentHorizontalSpeed, _currentVerticalSpeed) * Time.deltaTime;
            Gizmos.DrawWireCube(transform.position + move, _characterBounds.size);
        }

        #endregion

        #region Walk

        [Header("WALKING")]

        [SerializeField, Tooltip("加速度")]
        private float _acceleration = 90;

        [SerializeField, Tooltip("最大移动速度")]
        private float _moveClamp = 13;

        [SerializeField, Tooltip("减速度")]
        private float _deAcceleration = 60f;

        [SerializeField, Tooltip("在跳跃中对移速的加成系数")]
        private float _apexBonus = 2;

        /// <summary>
        /// 根据按键和碰撞情况，修改Player水平速度，实现左右移动
        /// 影响参数：_currentHorizontalSpeed
        /// </summary>
        private void CalculateWalk()
        {
            // 有“Horizontal”按键按下
            if (Input.X != 0)
            {
                // 设置水平速度，根据加速度加速
                _currentHorizontalSpeed += Input.X * _acceleration * Time.deltaTime;

                // 将速度限制在最大移动速度范围内
                _currentHorizontalSpeed = Mathf.Clamp(_currentHorizontalSpeed, -_moveClamp, _moveClamp);

                // 根据跳跃高度对速度给予加成
                float apexBonus = Mathf.Sign(Input.X) * _apexBonus * _apexPoint;
                _currentHorizontalSpeed += apexBonus * Time.deltaTime;
            }
            else
            {
                // 松开按键后，逐渐减速
                _currentHorizontalSpeed = Mathf.MoveTowards(_currentHorizontalSpeed, 0, _deAcceleration * Time.deltaTime);
            }

            // 如果左右两侧撞到墙壁，则将速度强制设成 0，不允许穿墙
            if (_currentHorizontalSpeed > 0 && _colRight || _currentHorizontalSpeed < 0 && _colLeft)
            {
                _currentHorizontalSpeed = 0;
            }
        }

        #endregion

        #region Gravity

        [Header("GRAVITY")]

        [SerializeField, Tooltip("最大下落速度")]
        private float _fallClamp = -40f;

        [SerializeField, Tooltip("最小下落加速度")]
        private float _minFallSpeed = 80f;

        [SerializeField, Tooltip("最大下落加速度")]
        private float _maxFallSpeed = 120f;

        private float _fallSpeed; // 当前下落加速度

        /// <summary>
        /// 实现重力
        /// </summary>
        private void CalculateGravity()
        {
            if (_colDown) // 说明落地了
            {
                // 落地后将垂直速度归零
                if (_currentVerticalSpeed < 0) _currentVerticalSpeed = 0;
            }
            else
            {
                // 当松开跳跃键并且此时还在上升，调大下落加速度使Player快速减速到下落状态
                float fallSpeed = _endedJumpEarly && _currentVerticalSpeed > 0 ? _fallSpeed * _jumpEndEarlyGravityModifier : _fallSpeed;

                // 依据当前下落加速度，修改当前垂直速度
                _currentVerticalSpeed -= fallSpeed * Time.deltaTime;

                // 因为有最大下落速度的限制，向下时不能快过最大下落速度
                if (_currentVerticalSpeed < _fallClamp) _currentVerticalSpeed = _fallClamp;
            }
        }

        #endregion

        #region Jump

        [Header("JUMPING")]

        [SerializeField, Tooltip("跳跃初速度")]
        private float _jumpHeight = 30;

        [SerializeField, Tooltip("")]
        private float _jumpApexThreshold = 10f;

        [SerializeField, Tooltip("离开平台边缘仍可起跳的时间")]
        private float _coyoteTimeThreshold = 0.1f;

        [SerializeField, Tooltip("在离落地前多少时间内就可以响应跳跃按键")]
        private float _jumpBuffer = 0.1f;

        [SerializeField, Tooltip("中断跳跃时附加的乡下加速度倍数")]
        private float _jumpEndEarlyGravityModifier = 3;

        private bool _coyoteUsable; // 并没在跳跃中
        private bool _endedJumpEarly = true; // 是否中断了跳跃
        private float _apexPoint; // 起跳时为0，跳到最高点时为1
        private float _lastJumpPressed; // 上次按下跳跃键的时间

        // 是否脱离平台边缘并且可以跳起
        private bool CanUseCoyote => _coyoteUsable && !_colDown && _timeLeftGrounded + _coyoteTimeThreshold > Time.time;

        // 是否在落地后自动跳起
        private bool HasBufferedJump => _colDown && _lastJumpPressed + _jumpBuffer > Time.time;

        /// <summary>
        /// 根据跳跃的程度，调整向下的加速度
        /// 越接近跳跃的最高点（即Velocity.y -> 0）时，_apexPoint -> 1
        /// 向下的加速度也受_apexPoint影响，_apexPoint -> 1，_fallSpeed -> max
        /// </summary>
        private void CalculateJumpApex()
        {
            if (!_colDown)
            {
                _apexPoint = Mathf.InverseLerp(_jumpApexThreshold, 0, Mathf.Abs(Velocity.y));
                _fallSpeed = Mathf.Lerp(_minFallSpeed, _maxFallSpeed, _apexPoint);
            }
            else
            {
                _apexPoint = 0;
            }
        }

        /// <summary>
        /// 处理跳跃
        /// </summary>
        private void CalculateJump()
        {
            // 如果 按下跳跃键且处于CanUseCoyote时，或者 处于HasBufferedJump时，跳跃
            if ((Input.JumpDown && CanUseCoyote) || HasBufferedJump)
            {
                _currentVerticalSpeed = _jumpHeight; // 设置初始速度
                _endedJumpEarly = false;             // 并未中断跳跃
                _coyoteUsable = false;               // 已经在跳跃中，使CanUseCoyote一定为false
                _timeLeftGrounded = float.MinValue;  // -3.40282347E+38，使CanUseCoyote一定为false
                JumpingThisFrame = true;             // 在当前帧跳跃了
            }
            else
            {
                JumpingThisFrame = false; // 在当前帧没有跳跃
            }

            // 如果当前帧松开了跳跃键，并且此时Player还在上升，则说明是中断跳跃
            if (!_colDown && Input.JumpUp && !_endedJumpEarly && Velocity.y > 0)
            {
                // 这里可以粗暴的将垂直速度设为0，但是这样手感不好，我们不这样做
                // _currentVerticalSpeed = 0;
                _endedJumpEarly = true;
            }

            // 如果向上撞到了障碍物，得强制速度为零，
            if (_colUp)
            {
                if (_currentVerticalSpeed > 0) _currentVerticalSpeed = 0;
            }
        }

        #endregion

        #region Move

        [Header("MOVE")]

        [SerializeField, Tooltip("碰撞检测精度")]
        private int _freeColliderIterations = 10;

        /// <summary>
        /// 移动角色
        /// </summary>
        private void MoveCharacter()
        {
            Vector3 pos = transform.position;

            // 根据Player当前帧的水平、垂直移动速度计算出下一帧应处于的位置
            RawMovement = new Vector3(_currentHorizontalSpeed, _currentVerticalSpeed);
            Vector3 move = RawMovement * Time.deltaTime;
            Vector3 furthestPoint = pos + move;

            // 如果没有发生碰撞，则可以直接移动Player
            var hit = Physics2D.OverlapBox(furthestPoint, _characterBounds.size, 0, _groundLayer);
            if (!hit)
            {
                transform.position += move;
                return;
            }

            // 否则我们要根据_freeColliderIterations，将原本一帧的移动拆分成若干更小的几步，逐步移动。
            Vector3 positionToMoveTo = transform.position;
            for (int i = 1; i < _freeColliderIterations; i++)
            {
                float t = (float)i / _freeColliderIterations;
                Vector2 posToTry = Vector2.Lerp(pos, furthestPoint, t);

                if (Physics2D.OverlapBox(posToTry, _characterBounds.size, 0, _groundLayer))
                {
                    transform.position = positionToMoveTo;

                    // 这说明我们差一点就跳上一个平台，可以轻推一下Player，让其可以跳上平台
                    // 或者起跳时头顶碰到了平台的角，可以轻轻让Player再靠外一点，不会被平台挡住跳跃
                    if (i == 1)
                    {
                        if (_currentVerticalSpeed < 0) _currentVerticalSpeed = 0;
                        Vector3 dir = transform.position - hit.transform.position;
                        transform.position += dir.normalized * move.magnitude;
                    }

                    return;
                }

                positionToMoveTo = posToTry;
            }
        }

        #endregion
    }
}

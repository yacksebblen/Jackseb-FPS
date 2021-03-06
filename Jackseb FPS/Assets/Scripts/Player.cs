﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using Photon.Pun;
using TMPro;

namespace Com.Jackseb.FPS
{
	public class Player : MonoBehaviourPunCallbacks, IPunObservable
	{
		#region Variables

		public float speed;
		public float sprintModifier;
		public float crouchModifier;
		public float jumpForce;
		public float cameraTransformAmount;
		public float t_hMove;
		public float t_vMove;
		public int maxHealth;
		public Camera normalCam;
		public Camera weaponCam;
		public GameObject cameraParent;
		public Transform weaponParent;
		public Transform groundDetector;
		public LayerMask ground;

		[HideInInspector] public ProfileData playerProfile;
		public TextMeshPro playerUsernameText;

		public float crouchAmount;
		public GameObject standingCollider;
		public GameObject crouchingCollider;

		// For the username text
		//private GameObject savedPlayer = null;

		private Transform uiHealthBar;
		private Text uiHealthAmount;
		private Text uiAmmo;
		private Text uiAmmoFrame;
		private Text uiUsername;
		private Image uiDamageIndicator;

		private Rigidbody rig;

		private Vector3 targetWeaponBobPosition;
		private Vector3 weaponParentOrigin;
		private Vector3 weaponParentCurrentPos;

		private float movementCounter;
		private float idleCounter;

		private float baseFOV;
		private float sprintFOVModifier = 1.25f;
		private Vector3 origin;

		public AudioSource footsteps;
		public AudioClip footstepSFX;
		public float footstepInterv = 0.32f;
		private float timeStamp;
		private bool doingFootsteps = false;

		private int currentHealth;

		private GameManager r_GameManager;
		private Weapon r_Weapon;

		public bool jumped;
		public bool crouched;

		private bool isAiming;

		private float aimAngle;

		private Vector3 normalCamTarget;
		private Vector3 weaponCamTarget;

		#endregion


		#region Photon Callbacks

		public void OnPhotonSerializeView(PhotonStream p_stream, PhotonMessageInfo p_message)
		{
			if (p_stream.IsWriting)
			{
				p_stream.SendNext((int)(weaponParent.transform.localEulerAngles.x * 100f));

				p_stream.SendNext(rig.position);
				p_stream.SendNext(rig.rotation);
				p_stream.SendNext(rig.velocity);
			}
			else
			{
				aimAngle = (int)p_stream.ReceiveNext() / 100f;

				rig.position = (Vector3)p_stream.ReceiveNext();
				rig.rotation = (Quaternion)p_stream.ReceiveNext();
				rig.velocity = (Vector3)p_stream.ReceiveNext();

				float lag = Mathf.Abs((float)(PhotonNetwork.Time - p_message.SentServerTime));
				rig.position += rig.velocity * lag;
			}
		}

		#endregion


		#region MonoBehavior Callbacks

		private void Start()
		{
			r_GameManager = GameObject.Find("GameManager").GetComponent<GameManager>();
			r_Weapon = GetComponent<Weapon>();
			currentHealth = maxHealth;

			cameraParent.SetActive(photonView.IsMine);
			if (!photonView.IsMine)
			{
				gameObject.layer = 11;
				ChangeLayersRecursively(standingCollider, 11);
				ChangeLayersRecursively(crouchingCollider, 11);
			}

			baseFOV = normalCam.fieldOfView;

			origin = normalCam.transform.localPosition;

			rig = GetComponent<Rigidbody>();
			weaponParentOrigin = weaponParent.localPosition;
			weaponParentCurrentPos = weaponParentOrigin;


			if (photonView.IsMine)
			{
				uiHealthBar = GameObject.Find("HUD/Health/Bar").transform;
				uiHealthAmount = GameObject.Find("HUD/Health/Health Amount").GetComponent<Text>();
				uiAmmo = GameObject.Find("HUD/Ammo/Text").GetComponent<Text>();
				uiAmmoFrame = GameObject.Find("HUD/Ammo/Frame").GetComponent<Text>();
				uiUsername = GameObject.Find("HUD/Health/Username").GetComponent<Text>();
				uiDamageIndicator = GameObject.Find("HUD/Damage Indicator").GetComponent<Image>();

				uiDamageIndicator.enabled = false;

				RefreshHealthBar();
				uiUsername.text = Launcher.myProfile.username;

				photonView.RPC("SyncProfile", RpcTarget.All, Launcher.myProfile.convertToObjArr());
			}
		}

		private void Update()
		{
			if (!photonView.IsMine)
			{
				RefreshMultiplayerState();
				return;
			}
			
			// Axes
			t_hMove = Input.GetAxis("Horizontal");
			t_vMove = Input.GetAxis("Vertical");

			// Controls
			bool sprint = Input.GetKey(KeyCode.LeftShift);
			bool jump = Input.GetKeyDown(KeyCode.Space);
			bool crouch = Input.GetKeyDown(KeyCode.LeftControl);
			bool pause = Input.GetKeyDown(KeyCode.Escape);

			// States
			bool isGrounded = Physics.Raycast(groundDetector.position, Vector3.down, 0.2f, ground);
			bool isJumping = jump && isGrounded;
			jumped = !isGrounded;
			//bool isSprinting = sprint && t_vMove > 0 && !isJumping && isGrounded;
			bool isSprinting = sprint && t_vMove > 0;
			bool isCrouching = crouch && !isSprinting && !isJumping && isGrounded;

			// Pause
			if (pause)
			{
				GameObject.Find("Pause").GetComponent<Pause>().TogglePause();
			}
			if (Pause.paused)
			{
				t_hMove = 0f;
				t_vMove = 0f;
				sprint = false;
				jump = false;
				pause = false;
				isGrounded = false;
				isJumping = false;
				isSprinting = false;
			}

			// Crouching
			if (isCrouching)
			{
				photonView.RPC("SetCrouch", RpcTarget.AllBuffered, !crouched);
			}

			// Jumping
			if (isJumping)
			{
				if (crouched) photonView.RPC("SetCrouch", RpcTarget.AllBuffered, false);
				if (r_Weapon.currentGunData != null) jumpForce = r_Weapon.currentGunData.playerJump;

				rig.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
			}

			// Headbob
			if (!isGrounded)
			{
				//airborne
				Headbob(idleCounter, 0.01f, 0.01f);
				idleCounter += 0;
				weaponParent.localPosition = Vector3.MoveTowards(weaponParent.localPosition, targetWeaponBobPosition, Time.deltaTime * 2f);
			}
			else if (t_hMove == 0 && t_vMove == 0)
			{
				//idling
				Headbob(idleCounter, 0.01f, 0.01f);
				idleCounter += Time.deltaTime * 3f;
				weaponParent.localPosition = Vector3.MoveTowards(weaponParent.localPosition, targetWeaponBobPosition, Time.deltaTime * 3f);
			}
			else if (!isSprinting)
			{
				//walking
				Headbob(movementCounter, 0.02f, 0.02f);
				movementCounter += Time.deltaTime * 3f;
				weaponParent.localPosition = Vector3.MoveTowards(weaponParent.localPosition, targetWeaponBobPosition, Time.deltaTime * 3f);
			}
			else
			{
				//sprinting
				Headbob(movementCounter, 0.15f, 0.055f);
				movementCounter += Time.deltaTime * 13.5f;
				weaponParent.localPosition = Vector3.MoveTowards(weaponParent.localPosition, targetWeaponBobPosition, Time.deltaTime * 10f);
			}

			//UI Refreshes
			RefreshHealthBar();
			if (r_Weapon.currentGunData != null && r_Weapon.currentGunData.needsReload)
			{
				uiAmmo.enabled = true;
				uiAmmoFrame.enabled = true;
				r_Weapon.RefreshAmmo(uiAmmo, uiAmmoFrame);
			}
			else
			{
				uiAmmo.enabled = false;
				uiAmmoFrame.enabled = false;
			}
		}

		private void FixedUpdate()
		{
			if (!photonView.IsMine) return;

			// Axes
			t_hMove = Input.GetAxis("Horizontal");
			t_vMove = Input.GetAxis("Vertical");

			// Controls
			bool sprint = Input.GetKey(KeyCode.LeftShift);
			bool jump = Input.GetKeyDown(KeyCode.Space);
			bool aim = Input.GetMouseButton(1);

			// States
			bool isGrounded = Physics.Raycast(groundDetector.position, Vector3.down, 0.2f, ground);
			bool isJumping = jump && isGrounded;
			jumped = !isGrounded;
			//bool isSprinting = sprint && t_vMove > 0 && !isJumping && isGrounded;
			bool isSprinting = sprint && t_vMove > 0;
			isAiming = aim && !Input.GetKey(KeyCode.LeftShift);

			// Pause
			if (Pause.paused)
			{
				t_hMove = 0f;
				t_vMove = 0f;
				sprint = false;
				jump = false;
				isGrounded = false;
				isJumping = false;
				isSprinting = false;
				isAiming = false;
			}

			// Movement
			Vector3 t_direction = Vector3.zero;
			float t_adjustedSpeed = speed;
			if (r_Weapon.currentGunData != null) t_adjustedSpeed = r_Weapon.currentGunData.playerSpeed;

			t_direction = new Vector3(t_hMove, 0, t_vMove);
			t_direction.Normalize();
			t_direction = transform.TransformDirection(t_direction);

			if (isSprinting)
			{
				if (crouched) photonView.RPC("SetCrouch", RpcTarget.AllBuffered, false);
				t_adjustedSpeed *= sprintModifier;
			}
			else if (crouched)
			{
				t_adjustedSpeed *= crouchModifier;
			}

			// Sound
			if (isSprinting && isGrounded && !doingFootsteps)
			{
				doingFootsteps = true;
				timeStamp = Time.time + footstepInterv;
			}
			else if (!isSprinting || !isGrounded)
			{
				doingFootsteps = false;
			}
			if (timeStamp <= Time.time && doingFootsteps)
			{
				photonView.RPC("SprintSound", RpcTarget.All);
				doingFootsteps = false;
			}

			Vector3 t_targetVelocity = t_direction * t_adjustedSpeed * Time.deltaTime;
			t_targetVelocity.y = rig.velocity.y;
			rig.velocity = t_targetVelocity;

			// Aiming
			isAiming = r_Weapon.Aim(isAiming);

			// Camera Stuff
			if (isSprinting)
			{
				normalCam.fieldOfView = Mathf.Lerp(normalCam.fieldOfView, baseFOV * sprintFOVModifier, Time.deltaTime * 8f);
				weaponCam.fieldOfView = Mathf.Lerp(weaponCam.fieldOfView, baseFOV * sprintFOVModifier, Time.deltaTime * 8f);
			}
			else if (isAiming)
			{
				normalCam.fieldOfView = Mathf.Lerp(normalCam.fieldOfView, baseFOV * r_Weapon.currentGunData.mainFOV, Time.deltaTime * 8f);
				weaponCam.fieldOfView = Mathf.Lerp(weaponCam.fieldOfView, baseFOV * r_Weapon.currentGunData.weaponFOV, Time.deltaTime * 8f);
			}
			else
			{
				normalCam.fieldOfView = Mathf.Lerp(normalCam.fieldOfView, baseFOV, Time.deltaTime * 8f);
				weaponCam.fieldOfView = Mathf.Lerp(weaponCam.fieldOfView, baseFOV, Time.deltaTime * 8f);
			}

			if (crouched)
			{
				normalCamTarget = Vector3.MoveTowards(normalCam.transform.localPosition, origin + Vector3.down * crouchAmount, Time.deltaTime * 4f);
				weaponCamTarget = Vector3.MoveTowards(weaponCam.transform.localPosition, origin + Vector3.down * crouchAmount, Time.deltaTime * 4f);
			}
			else
			{
				normalCamTarget = Vector3.MoveTowards(normalCam.transform.localPosition, origin, Time.deltaTime * 4f);
				weaponCamTarget = Vector3.MoveTowards(weaponCam.transform.localPosition, origin, Time.deltaTime * 4f);
			}

			// Show and hide username
			//RaycastHit t_hit = new RaycastHit();

			//if (photonView.IsMine)
			//{
			//	if (Physics.Raycast(normalCam.transform.position, normalCam.transform.forward, out t_hit, 1000f))
			//	{
			//		if (t_hit.collider.gameObject.layer == 11)
			//		{
			//			if (savedPlayer == null)
			//			{
			//				savedPlayer = t_hit.collider.transform.root.gameObject;
			//			}
			//			else
			//			{
			//				savedPlayer.transform.Find("Username").GetComponent<MeshRenderer>().enabled = true;
			//				savedPlayer.transform.Find("Username Frame").GetComponent<MeshRenderer>().enabled = true;
			//			}
			//		}
			//		else
			//		{
			//			if (savedPlayer != null)
			//			{
			//				savedPlayer.transform.Find("Username").GetComponent<MeshRenderer>().enabled = false;
			//				savedPlayer.transform.Find("Username Frame").GetComponent<MeshRenderer>().enabled = false;
			//				savedPlayer = null;
			//			}
			//		}
			//	}
			//}
		}

		private void LateUpdate()
		{
			normalCam.transform.localPosition = normalCamTarget;
			weaponCam.transform.localPosition = weaponCamTarget;
		}

		#endregion


		#region Private Methods

		void ChangeLayersRecursively(GameObject p_target, int p_layer)
		{
			p_target.layer = p_layer;
			foreach (Transform a in p_target.transform) ChangeLayersRecursively(a.gameObject, p_layer);
		}

		void RefreshMultiplayerState()
		{
			float cacheEulY = weaponParent.localEulerAngles.y;

			Quaternion targetRotation = Quaternion.identity * Quaternion.AngleAxis(aimAngle, Vector3.right);
			weaponParent.rotation = Quaternion.Slerp(weaponParent.rotation, targetRotation, Time.deltaTime * 6f);

			Vector3 finalRotation = weaponParent.localEulerAngles;
			finalRotation.y = cacheEulY;

			weaponParent.localEulerAngles = finalRotation;
		}

		void Headbob(float p_z, float p_xIntensity, float p_yIntensity)
		{
			float t_aimAdjust = 1f;
			if (isAiming) t_aimAdjust = 0.01f;
			targetWeaponBobPosition = weaponParentCurrentPos + new Vector3(Mathf.Cos(p_z) * p_xIntensity * t_aimAdjust, Mathf.Sin(p_z * 2) * p_yIntensity * t_aimAdjust, 0);
		}

		void RefreshHealthBar()
		{
			float t_healthRatio = (float)currentHealth / (float)maxHealth;

			uiHealthBar.localScale = Vector3.Lerp(uiHealthBar.localScale, new Vector3(t_healthRatio, 1, 1), Time.deltaTime * 8f);
			uiHealthAmount.text = Mathf.RoundToInt(uiHealthBar.localScale.x * 100).ToString();

			if (currentHealth >= 50 && currentHealth <= 100)
			{
				uiHealthBar.GetComponent<Image>().color = Color.Lerp(uiHealthBar.GetComponent<Image>().color, Color.green, Time.deltaTime * 8f);
			}
			else if (currentHealth > 20 && currentHealth < 50)
			{
				uiHealthBar.GetComponent<Image>().color = Color.Lerp(uiHealthBar.GetComponent<Image>().color, Color.yellow, Time.deltaTime * 8f);
			}
			else if (currentHealth > 0 && currentHealth <= 20)
			{
				uiHealthBar.GetComponent<Image>().color = Color.Lerp(uiHealthBar.GetComponent<Image>().color, Color.red, Time.deltaTime * 8f);
			}
		}

		[PunRPC]
		void SprintSound()
		{
			if (photonView.IsMine)
			{
				footsteps.spatialBlend = 0;
			}
			else
			{
				footsteps.spatialBlend = 1;
			}
			footsteps.PlayOneShot(footstepSFX);
		}

		[PunRPC]
		void SetCrouch (bool p_state)
		{
			if (crouched == p_state) return;

			crouched = p_state;

			if (crouched)
			{
				standingCollider.SetActive(false);
				crouchingCollider.SetActive(true);
				weaponParentCurrentPos += Vector3.down * crouchAmount;
			}
			else
			{
				standingCollider.SetActive(true);
				crouchingCollider.SetActive(false);
				weaponParentCurrentPos -= Vector3.down * crouchAmount;
			}
		}

		[PunRPC]
		private void SyncProfile(object[] arrOfObj)
		{
			playerProfile = new ProfileData(arrOfObj);
			playerUsernameText.text = playerProfile.username;
		}

		#endregion


		#region Public Methods

		public void TakeDamage(int p_damage, int p_actor, float p_multi)
		{
			if (photonView.IsMine)
			{
				currentHealth -= Mathf.RoundToInt(p_damage * p_multi);
				RefreshHealthBar();
				if (p_damage > 0)
				{
					StartCoroutine(DamageIndicator(0.1f));
				}

				if (currentHealth <= 0)
				{
					r_GameManager.Spawn();
					r_GameManager.ChangeStat_S(PhotonNetwork.LocalPlayer.ActorNumber, 1, 1);

					if (p_actor >= 0 && p_actor != PhotonNetwork.LocalPlayer.ActorNumber)
						r_GameManager.ChangeStat_S(p_actor, 0, 1);

					PhotonNetwork.Destroy(gameObject);
				}
				else if (currentHealth > 100)
				{
					currentHealth = 100;
				}
			}
		}

		IEnumerator DamageIndicator(float p_wait)
		{
			uiDamageIndicator.enabled = true;

			yield return new WaitForSeconds(p_wait);

			uiDamageIndicator.enabled = false;
		}

		#endregion
	}

}
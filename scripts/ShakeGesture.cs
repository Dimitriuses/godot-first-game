using Godot;

/// <summary>
/// Decides whether the device has just been shaken, from a stream of accelerometer
/// samples. Nothing to do with the board's visual shudder — that is
/// <c>GameManager.StartShake</c>, and this is a way of asking for it.
///
/// A plain class rather than a node, and fed rather than polling, for two reasons. It can
/// be tested by handing it a made-up sequence of samples, which a node reading
/// <c>Input.GetAccelerometer()</c> could not be. And the source of those samples differs
/// per platform: an Android or iOS export has the accelerometer through Godot, while a
/// web build has to be handed values from the browser's `devicemotion` event, because
/// Godot's web platform does not implement the sensor APIs. Both can call Feed.
/// </summary>
public sealed class ShakeGesture
{
	/// How much sharper than gravity a jolt has to be, in m/s². About 2g: enough that
	/// putting a phone down on a table does not throw the dice.
	public float Trigger = 19f;

	/// The quietest a shake may end before another one counts, so one wobble of a wrist
	/// is one throw rather than five.
	public double CooldownSeconds = 1.2;

	/// Seconds for the running estimate of "which way is down" to catch up. Long enough
	/// that a shake does not become the baseline it is measured against.
	private const double GravityCatchUp = 0.7;

	private Vector3 gravity;
	private bool seeded;
	private double sinceFired = double.MaxValue;

	/// <summary>
	/// Take one sample. True exactly once per shake.
	///
	/// The first sample only seeds the baseline. Without that, a detector starting from
	/// zero sees the whole of gravity as a jolt and fires the moment it is switched on.
	/// </summary>
	public bool Feed(Vector3 acceleration, double delta)
	{
		if (delta <= 0)
			return false;

		if (!seeded)
		{
			gravity = acceleration;
			seeded = true;
			return false;
		}

		// A low pass on the raw reading is "down"; whatever is left is the shaking.
		float catchUp = (float)Mathf.Min(1.0, delta / GravityCatchUp);
		gravity = gravity.Lerp(acceleration, catchUp);
		float jolt = (acceleration - gravity).Length();

		sinceFired += delta;
		if (jolt < Trigger || sinceFired < CooldownSeconds)
			return false;

		sinceFired = 0;
		return true;
	}

	/// Forget everything. For a test, or for a device that has stopped reporting.
	public void Reset()
	{
		seeded = false;
		sinceFired = double.MaxValue;
	}
}

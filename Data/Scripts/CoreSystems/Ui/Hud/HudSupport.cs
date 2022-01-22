using System.Collections.Generic;
using CoreSystems.Platform;
using CoreSystems.Support;
using VRage.Utils;
using VRageMath;

namespace WeaponCore.Data.Scripts.CoreSystems.Ui.Hud
{
    partial class Hud
    {
        internal uint TicksSinceUpdated => _session.Tick - _lastHudUpdateTick;
        internal bool KeepBackground => _session.Tick - _lastHudUpdateTick < MinUpdateTicks;

        internal void UpdateHudSettings()
        {
            //runs once on first draw then only again if a menu is closed
            var fovScale = (float)(0.1 * _session.ScaleFov);

            var fovModifier = (float)((_session.Settings.ClientConfig.HudScale * 1.4) * _session.ScaleFov);
            var normScaler = (float)(_session.Settings.ClientConfig.HudScale * _session.ScaleFov);
            var aspectScale = (2.37037f / _session.AspectRatio);

            NeedsUpdate = false;
            _lastHudUpdateTick = 0;
            _viewPortSize.X = (fovScale * _session.AspectRatio);
            _viewPortSize.Y = fovScale;
            _viewPortSize.Z = -0.1f;

            _currWeaponDisplayPos.X = _viewPortSize.X * BgWidthPosOffset;
            _currWeaponDisplayPos.Y = _viewPortSize.Y * .6f;

            _padding = PaddingConst * ((float)_session.ScaleFov * _session.AspectRatio);
            _reloadWidth = ReloadWidthConst * fovModifier;
            _reloadHeight = ReloadHeightConst * fovModifier;
            _reloadOffset = _reloadWidth * fovModifier;

            _textSize = WeaponHudFontHeight * fovModifier;
            _sTextSize = _textSize * .75f;
            _textWidth = (WeaponHudFontHeight * _session.AspectRatioInv) * fovScale;
            _stextWidth = (_textWidth * .75f);
            _stackPadding = _stextWidth * 6; // gives max limit of 6 characters (x999)

            _heatWidth = (HeatWidthConst * fovModifier) ;
            _heatHeight = HeatHeightConst * fovModifier;
            _heatOffsetX = (HeatWidthOffset * fovModifier) * aspectScale;
            _heatOffsetY = (_heatHeight * 3f);

            _infoPaneloffset = InfoPanelOffset * normScaler;
            //_paddingHeat = _session.CurrentFovWithZoom < 1 ? MathHelper.Clamp(_session.CurrentFovWithZoom * 0.0001f, 0.0001f, 0.0003f) : 0;
            _paddingReload = _session.CurrentFovWithZoom < 1 ? MathHelper.Clamp(_session.CurrentFovWithZoom * 0.002f, 0.0002f, 0.001f) : 0.001f;

            _symbolWidth = ((_heatWidth + _padding) * aspectScale);
            _bgColor = new Vector4(1f, 1f, 1f, 0f);
        }

        internal void AddText(string text, float x, float y, long elementId, int ttl, Vector4 color, Justify justify = Justify.None, FontType fontType = FontType.Shadow, float fontSize = 10f, float heightScale = 0.65f)
        {
            if (_agingTextRequests.ContainsKey(elementId) || string.IsNullOrEmpty(text))
                return;

            AgingTextures = true;

            var request = _agingTextRequestPool.Get();

            var pos = GetScreenSpace(new Vector2(x, y));
            request.Text = text;
            request.Color = color;
            request.Position.X = pos.X;
            request.Position.Y = pos.Y;
            request.FontSize = fontSize * MetersInPixel;
            request.Font = fontType;
            request.Ttl = ttl;
            request.ElementId = elementId;
            request.Justify = justify;
            request.HeightScale = ShadowHeightScaler;
            _agingTextRequests.TryAdd(elementId, request);
        }

        internal Vector2 GetScreenSpace(Vector2 offset)
        {
            var fovScale = (float)(0.1 * _session.ScaleFov);

            var position = new Vector2(offset.X, offset.Y);
            position.X *= fovScale * _session.AspectRatio;
            position.Y *= fovScale;
            return position;
        }

        internal List<StackedWeaponInfo> SortDisplayedWeapons(List<Weapon> list)
        {
            int length = list.Count;
            int finalCount = 0;
            List<StackedWeaponInfo> finalList;
            if (!_weaponInfoListPool.TryDequeue(out finalList))
                finalList = new List<StackedWeaponInfo>();

            for (int h = length / 2; h > 0; h /= 2) // switch a proper comparer, performance not an issue.  Sort by heat duration and then estimated time to reload complete, longest to shortest
            {
                for (int i = h; i < length; i += 1)
                {
                    var tempValue = list[i];
                    var temp = list[i].PartState.Heat;

                    int j;
                    for (j = i; j >= h && list[j - h].PartState.Heat < temp; j -= h)
                    {
                        list[j] = list[j - h];
                    }

                    list[j] = tempValue;
                }
            }

            if (list.Count > WeaponLimit) //limit to top 50 based on heat
                list.RemoveRange(WeaponLimit, list.Count - WeaponLimit);
            else if (list.Count <= StackThreshold)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var w = list[i];
                    if (w.System.PartName.Length > _currentLargestName) _currentLargestName = w.System.PartName.Length;

                    StackedWeaponInfo swi;
                    if (!_weaponStackedInfoPool.TryDequeue(out swi))
                        swi = new StackedWeaponInfo();

                    if (!_textureDrawPool.TryDequeue(out swi.CachedReloadTexture))
                        swi.CachedReloadTexture = new TextureDrawData();

                    if (!_textureDrawPool.TryDequeue(out swi.CachedHeatTexture))
                        swi.CachedHeatTexture = new TextureDrawData();

                    swi.CachedHeatTexture.Persistant = true;
                    swi.CachedReloadTexture.Persistant = true;
                    swi.ReloadIndex = 0;
                    swi.HighestValueWeapon = w;
                    swi.WeaponStack = 1;
                    finalList.Add(swi);
                }
                return finalList;
            }


            Dictionary<int, List<Weapon>> weaponTypes = new Dictionary<int, List<Weapon>>();
            for (int i = 0; i < list.Count; i++) //sort list into groups of same weapon type
            {
                var w = list[i];

                if (!weaponTypes.ContainsKey(w.System.WeaponIdHash))
                {
                    List<Weapon> tmp;
                    if (!_weaponSortingListPool.TryDequeue(out tmp))
                        tmp = new List<Weapon>();

                    weaponTypes[w.System.WeaponIdHash] = tmp;
                }

                weaponTypes[w.System.WeaponIdHash].Add(w);
            }

            foreach (var weaponType in weaponTypes)
            {
                var weapons = weaponType.Value;
                if (weapons[0].System.PartName.Length > _currentLargestName) _currentLargestName = weapons[0].System.PartName.Length;


                if (weapons.Count > 1)
                {
                    List<List<Weapon>> subLists;
                    List<Weapon> subList;
                    var last = weapons[0];

                    if (!_weaponSubListsPool.TryDequeue(out subLists))
                        subLists = new List<List<Weapon>>();

                    if (!_weaponSortingListPool.TryDequeue(out subList))
                        subList = new List<Weapon>();

                    for (int i = 0; i < weapons.Count; i++)
                    {
                        var w = weapons[i];

                        if (i == 0)
                            subList.Add(w);
                        else
                        {
                            if (last.HeatPerc - w.HeatPerc > .05f || last.Loading != w.Loading || last.Reload.WaitForClient != w.Reload.WaitForClient || last.PartState.Overheated != w.PartState.Overheated)
                            {
                                subLists.Add(subList);
                                if (!_weaponSortingListPool.TryDequeue(out subList))
                                    subList = new List<Weapon>();
                            }

                            last = w;
                            subList.Add(w);

                            if (i == weapons.Count - 1)
                                subLists.Add(subList);
                        }
                    }

                    weapons.Clear();
                    _weaponSortingListPool.Enqueue(weapons);

                    for (int i = 0; i < subLists.Count; i++)
                    {
                        var subL = subLists[i];

                        if (finalCount < StackThreshold)
                        {
                            StackedWeaponInfo swi;
                            if (!_weaponStackedInfoPool.TryDequeue(out swi))
                                swi = new StackedWeaponInfo();

                            if (!_textureDrawPool.TryDequeue(out swi.CachedReloadTexture))
                                swi.CachedReloadTexture = new TextureDrawData();

                            if (!_textureDrawPool.TryDequeue(out swi.CachedHeatTexture))
                                swi.CachedHeatTexture = new TextureDrawData();

                            swi.CachedHeatTexture.Persistant = true;
                            swi.CachedReloadTexture.Persistant = true;
                            swi.ReloadIndex = 0;
                            swi.HighestValueWeapon = subL[0];
                            swi.WeaponStack = subL.Count;

                            finalList.Add(swi);
                            finalCount++;
                        }

                        subL.Clear();
                        _weaponSortingListPool.Enqueue(subL);
                    }

                    subLists.Clear();
                    _weaponSubListsPool.Enqueue(subLists);
                }
                else
                {
                    if (finalCount < StackThreshold)
                    {
                        StackedWeaponInfo swi;
                        if (!_weaponStackedInfoPool.TryDequeue(out swi))
                            swi = new StackedWeaponInfo();

                        if (!_textureDrawPool.TryDequeue(out swi.CachedReloadTexture))
                            swi.CachedReloadTexture = new TextureDrawData();

                        if (!_textureDrawPool.TryDequeue(out swi.CachedHeatTexture))
                            swi.CachedHeatTexture = new TextureDrawData();

                        swi.CachedHeatTexture.Persistant = true;
                        swi.CachedReloadTexture.Persistant = true;
                        swi.ReloadIndex = 0;

                        swi.HighestValueWeapon = weapons[0];
                        swi.WeaponStack = 1;


                        finalList.Add(swi);
                        finalCount++;
                    }

                    weapons.Clear();
                    _weaponSortingListPool.Enqueue(weapons);
                }
            }

            return finalList;
        }

        internal void Purge()
        {

            _textureDrawPool.Clear();
            _textDrawPool.Clear();
            _weaponSortingListPool.Clear();
            _weaponStackedInfoPool.Clear();
            CharacterMap.Clear();
            _textureAddList.Clear();
            _textAddList.Clear();
            _drawList.Clear();
            _weapontoDraw.Clear();
            WeaponsToDisplay.Clear();

            List<StackedWeaponInfo> removeList;
            while (_weaponInfoListPool.TryDequeue(out removeList))
                removeList.Clear();

            List<List<Weapon>> removeList1;
            while (_weaponSubListsPool.TryDequeue(out removeList1))
            {
                for (int i = 0; i < removeList1.Count; i++)
                    removeList1[i].Clear();

                removeList1.Clear();
            }
        }
    }
}

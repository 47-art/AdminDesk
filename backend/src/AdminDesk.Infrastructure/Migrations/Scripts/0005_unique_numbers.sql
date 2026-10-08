-- Master numbers are unique regardless of letter case and surrounding spaces. The serial number of an
-- asset was not unique before.
CREATE UNIQUE INDEX ux_sims_sim_number_ci ON sims(lower(trim(sim_number)));
CREATE UNIQUE INDEX ux_sims_mobile_number_ci ON sims(lower(trim(mobile_number)));
CREATE UNIQUE INDEX ux_assets_asset_tag_ci ON assets(lower(trim(asset_tag)));
CREATE UNIQUE INDEX ux_assets_serial_number_ci ON assets(lower(trim(serial_number)));
CREATE UNIQUE INDEX ux_id_cards_card_number_ci ON id_cards(lower(trim(card_number)));

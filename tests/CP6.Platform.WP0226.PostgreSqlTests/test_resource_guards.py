"""Offline negative lifecycle guards. No PostgreSQL command or service access."""
import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import pg_resource as pg


class ResourceGuards(unittest.TestCase):
    def test_unknown_session_blocks_before_any_shutdown(self):
        c = dict(Runtime='expected', Database='owned', Marker='CP6-WP02-26:token')
        with patch.object(pg, 'sql', return_value=json.dumps([dict(Pid=99, Role='other', Database='owned', Application=c['Marker']+':crash')])):
            with self.assertRaisesRegex(RuntimeError, 'Unexpected sessions'):
                pg.sessions(c, [100])
            with self.assertRaisesRegex(RuntimeError, 'Session owner'):
                pg.sessions(c, [99])

    def test_expected_pid_does_not_override_wrong_database_or_application(self):
        c = dict(Runtime='expected', Database='owned', Marker='CP6-WP02-26:token')
        good = dict(Pid=99, Role='expected', Database='owned', Application=c['Marker']+':crash')
        for key in ('Role','Database','Application'):
            row = copy.copy(good); row[key] = 'other'
            with patch.object(pg, 'sql', return_value=json.dumps([row])):
                with self.assertRaisesRegex(RuntimeError, 'Session owner'):
                    pg.sessions(c,[99])

    def test_wrong_settings_path_rejected_before_read(self):
        with patch.object(Path, 'read_bytes', side_effect=AssertionError('must not read')):
            with self.assertRaisesRegex(RuntimeError, 'Wrong settings'):
                pg.load('D:/CP6-local-test/WP02-08/unknown/settings.json')

    def test_missing_ready_identity_rejected_before_server_access(self):
        path = pg.ROOT/('a'*32)/'settings.json'
        for c in [dict(Stage='Started'),dict(Stage='Ready'),dict(Stage='Ready',SystemIdentifier='bad',DatabaseOid=12)]:
            with patch.object(Path, 'read_bytes', return_value=json.dumps(c).encode()), patch.object(pg, 'paths', return_value=path.parent):
                with self.assertRaises(RuntimeError):
                    pg.load(path)

    def test_marker_owner_format_rejects_path_escape(self):
        with self.assertRaisesRegex(RuntimeError,'Invalid owner'):
            pg.paths(dict(Owner='../another'))

    def test_changed_configuration_cannot_start_with_same_system_identifier(self):
        c=dict(Owner='a'*32,SystemIdentifier='123')
        seal=dict(Owner=c['Owner'],SystemIdentifier='123',Hashes={'postgresql.conf':'old'})
        with patch.object(pg,'paths',return_value=pg.ROOT/c['Owner']), patch.object(Path,'read_bytes',return_value=json.dumps(seal).encode()), patch.object(pg,'config_hashes',return_value={'postgresql.conf':'changed'}):
            with self.assertRaisesRegex(RuntimeError,'configuration seal'):
                pg.verify_config(c)


if __name__ == '__main__':
    unittest.main(verbosity=2)

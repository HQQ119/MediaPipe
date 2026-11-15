import io
import unittest
from control_channel import start_stdin_control


class ControlTests(unittest.TestCase):
    def test_quit_requests_stop(self):
        self.assertTrue(start_stdin_control(io.StringIO("quit\n")).wait(1))

    def test_pipe_closed_requests_stop(self):
        self.assertTrue(start_stdin_control(io.StringIO("")).wait(1))

    def test_ignores_unknown_command_until_quit(self):
        self.assertTrue(start_stdin_control(io.StringIO("status\nQUIT\n")).wait(1))
